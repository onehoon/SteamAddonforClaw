# Addon Quick Settings Overlay — UI / Navigation Design Baseline

> **Status:** Current UI design baseline / implementation planning document  
> **Date:** 2026-09-02  
> **Scope:** Visual shell, tab model, controller-first navigation, layout hierarchy, common control interaction, mutation/debounce behavior, and future Shortcut surface for `SteamInputAddonforClaw.Overlay.exe`.  
> **Not a work order:** This document defines the UI contract that later OQ5 work orders should implement in focused PRs.  
> **Implementation state:** Some underlying Overlay window/process/transport/capture foundations already exist or are being completed, but the full UI described here is not yet implemented or hardware-validated.

---

## 1. Read together with

This document is the UI-detail companion to the existing architecture and lifecycle documents.

Read together with:

- `docs/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ3_A_MAIN_UI_OVERLAY_VISIBLE_SURFACE_COEXISTENCE_WORK_ORDER.md`
- `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`
- current `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml`
- current `src/SteamInputAddonforClaw.QamHost/Frontend/qam.js`

This document does **not** replace the controller-authority or lifecycle architecture.

The hierarchy remains:

```text
Full PID1902 architecture
    owns controller / PID1902 / DirectInput / HidHide / VIIPER / presentation

OQ4 Overlay capture
    temporarily neutralizes game-facing controller publication
    and routes semantic controller input to Overlay

This UI design
    decides how the visible Overlay is organized and how semantic input behaves
```

The Overlay UI is never a second controller, feature, persistence, or hardware authority.

---

## 2. Current product correction — Steam QAM may remain behind the Addon Overlay

The UI design follows the newer OQ4 product decision:

```text
Steam QAM may already be visible
        ↓
Addon Overlay opens above it
        ↓
OQ4 capture keeps the current X360 / SteamDeck presentation neutral
        ↓
Steam QAM behind the Addon Overlay receives no controller navigation
        ↓
Addon Overlay closes
        ↓
underlying Steam QAM remains available
```

Therefore the UI implementation must **not** add a Steam-QAM-specific visibility manager just to build this shell.

Do not:

- close Steam QAM before every Overlay Show;
- stop/restart `QamHost.exe`;
- repurpose `.Qam` for Overlay;
- add QAM visibility polling;
- implement an OQ3-B surface manager.

Pointer/touch remains transient-surface behavior:

```text
pointer/touch inside Overlay
→ Overlay handles it

pointer/touch outside Overlay
→ existing outside-dismiss path
→ Runtime retires Overlay capture safely
→ underlying game / Steam QAM remains available
```

The Main UI ↔ Addon Overlay coexistence contract remains different: the normal Addon settings UI and the Addon Overlay should not intentionally stack as two Addon-owned control surfaces.

---

## 3. UI goal

The Overlay should become a controller-first handheld Quick Settings surface, not a condensed desktop Settings window.

Primary goals:

1. Open quickly and show useful current state at a glance.
2. Make the most common actions reachable with very few controller inputs.
3. Keep navigation predictable across all tabs.
4. Make sliders adjustable immediately without entering a separate edit mode.
5. Keep global controller meanings stable regardless of the current tab.
6. Preserve OQ4 neutral-output safety while the Overlay is visible.
7. Let future features be added without replacing the top-level navigation model.
8. Avoid a generalized dashboard/layout framework.

The expected interaction is:

```text
open Overlay
→ first configured tab is selected
→ first usable item is selected
→ DPad / either stick moves selection
→ Left / Right adjusts an adjustable row immediately
→ A activates/selects
→ LB / RB changes tabs
→ B always closes Overlay
```

---

## 4. Frozen top-level tab model

The Overlay starts with five top-level horizontal tabs:

```text
Device
Profile
Controller
Shortcut
Setting
```

Default order:

```text
1. Device
2. Profile
3. Controller
4. Shortcut
5. Setting
```

All five tabs must exist in the initial UI shell even if some pages contain only placeholder/empty-state content at first.

Do not delay the tab architecture until individual features are ready. The purpose of creating the five pages early is to stabilize:

- top-level navigation;
- page lifetime;
- logical selection ownership;
- tab order persistence;
- startup-tab behavior;
- future page-specific content layout.

### 4.1 Intended meaning of each tab

#### Device

Device-wide settings that apply independently of the active game/profile.

Initial/future examples:

- TDP;
- CPU Boost;
- Windows Power Mode;
- device-level FPS policy where applicable;
- fan/fan-curve controls after their Runtime contract exists;
- other supported board/device controls.

The Overlay must call the existing Runtime feature authorities. It must not create `OverlayTdpManager`, `OverlayCpuBoostManager`, another EC helper, or another settings store.

#### Profile

Current-game / active-profile quick settings.

Examples:

- active profile enabled/disabled;
- per-game TDP;
- per-game FPS limit;
- per-game CPU/Power behavior when supported;
- future profile-scoped settings already owned by Runtime.

If no active supported game/profile exists, show a deliberate empty/unavailable state rather than pretending Device settings are Profile settings.

#### Controller

Controller-specific quick settings and status that belong to the Addon controller feature set.

Potential future examples:

- controller options;
- vibration strength where supported;
- controller mapping-related quick actions where product policy allows;
- controller status relevant to normal users.

Do not expose internal routing/debug/VIIPER/HidHide diagnostics merely because this page exists.

#### Shortcut

A dedicated button/tile-style quick-action page, conceptually similar to a small Game Bar quick-action surface.

The long-term baseline is **four user-configurable shortcut slots**.

```text
Shortcut Slot 1
Shortcut Slot 2
Shortcut Slot 3
Shortcut Slot 4
```

The exact actions are future product work. The important UI architecture decision is that Shortcut is a first-class tab from the beginning.

#### Setting

Overlay-specific user preferences and lightweight presentation settings.

Potential examples:

- tab order;
- future Overlay presentation preferences;
- future Shortcut slot configuration entry point if appropriate.

This page is not a duplicate of the full desktop Settings UI. Rare, complex, setup, diagnostics, and maintenance workflows belong in the main UI.

---

## 5. Tab order is user-configurable

The five tabs are fixed identities, but their **order is user-configurable**.

Example:

```text
Default
[ Device ] [ Profile ] [ Controller ] [ Shortcut ] [ Setting ]

User preference
[ Controller ] [ Device ] [ Profile ] [ Shortcut ] [ Setting ]
```

The design should model the tab strip as an ordered collection from the beginning rather than hard-coding five independent positions into page logic.

This does **not** mean building a generalized dynamic-tab framework.

Frozen scope:

```text
known fixed tabs = Device / Profile / Controller / Shortcut / Setting
user changes only their order
```

Do not support:

- user-created tabs;
- tab deletion;
- arbitrary plugin tabs;
- nested tab trees;
- a generalized layout designer.

### 5.1 No separate “default tab” setting

There is intentionally no separate setting such as:

```text
DefaultOverlayTab = Controller
```

The first tab in the configured order **is** the startup tab.

Therefore:

```text
configured tab order[0]
        =
Overlay startup tab
```

This removes duplicate preference state and makes the UI self-explanatory: the tab the user puts first is the tab the Overlay opens to.

### 5.2 No last-tab restore across Overlay opens

The Overlay process is warm and may remain alive while hidden, but a new Show is a new Quick Settings interaction.

On **every successful Show**:

```text
selected tab = configured tab order[0]
```

Do not persist or restore the tab that happened to be selected when the previous Overlay session closed.

Examples:

```text
order = Controller, Device, Profile, Shortcut, Setting

Show #1 → Controller
user moves to Profile
Close
Show #2 → Controller again
```

This behavior is predictable and avoids another persisted state fact.

### 5.3 Tab-order persistence ownership

Tab order is a user preference, but the warm Overlay process should not invent an independent configuration file/store.

Preferred authority:

```text
Overlay UI
→ requests tab-order mutation
→ existing Runtime/settings authority persists the preference
→ Overlay receives authoritative order
```

The exact existing settings storage seam should be selected in the focused OQ5 implementation work order after current settings code is inspected.

Do not refactor unrelated settings infrastructure just to store five enum values.

### 5.4 Invalid/missing preference

Keep fallback simple.

If no valid stored order exists, use exactly:

```text
Device, Profile, Controller, Shortcut, Setting
```

If a future version adds/removes a known tab, normalize the known fixed list narrowly at the preference boundary. Do not build a migration framework in advance.

---

## 6. Horizontal tab-strip contract

Tabs are horizontal and remain visible while page content scrolls.

Conceptual shell:

```text
┌──────────────────────────────────────┐
│ Quick Settings                       │
│                                      │
│ Device Profile Controller Shortcut Setting
│ ━━━━━                                │
├──────────────────────────────────────┤
│                                      │
│ current tab content                  │
│                                      │
│                                      │
├──────────────────────────────────────┤
│ controller hints / status (optional) │
└──────────────────────────────────────┘
```

The tab strip should not scroll vertically with page content.

Preferred visual behavior:

- one clear selected-tab state;
- selected indicator may be underline/bar/accent treatment;
- inactive tabs remain readable but visually quieter;
- no close buttons;
- no drag handles in the ordinary Quick Settings surface;
- tab-order editing belongs in an explicit configuration surface, not normal tab navigation.

### 6.1 Five tabs at the current 400-DIP width

Current code uses:

```text
OverlayWindowGeometry.PocPanelWidthDip = 400
```

At the reference 150% DPI this is 600 physical pixels.

Five English text labels are a tighter fit than the existing POC content, especially `Controller` and `Shortcut`.

Initial policy:

1. Keep the current 400-DIP panel baseline unless hardware/UI evidence says it must change.
2. Keep standard WinUI control/typography sizing initially.
3. Let the tab strip use horizontal space more efficiently than the body content if necessary.
4. Do not globally scale down the entire UI simply to fit the tabs.
5. Do not add a horizontally scrolling tab bar for these five fixed tabs.
6. If standard `TabView` chrome is unnecessarily wide, use the simplest horizontal selector that preserves the desired WinUI appearance and logical selection; do not build a custom tab framework.

Exact final tab padding/font tuning is a hardware-polish decision, not an architecture requirement.

---

## 7. Global controller mapping

The following meanings are intended to remain stable across all tabs.

| Physical input | Overlay meaning | Initial policy |
| --- | --- | --- |
| `LB` | Previous tab | Fixed |
| `RB` | Next tab | Fixed |
| `LT` | Reserved | No action |
| `RT` | Reserved | No action |
| `B` | Close Overlay | Fixed global action |
| `A` | Select / Activate | Fixed semantic action |
| `X` | Reserved | No action |
| `Y` | Reserved | No action |
| DPad | Directional navigation / adjustment | Enabled |
| Left Stick | Directional navigation / adjustment | Enabled |
| Right Stick | Directional navigation / adjustment | Enabled |

This is controller-first behavior. Pointer/touch remains supported naturally by the WinUI controls and existing transient-surface dismissal contract.

### 7.1 LB / RB — tab navigation

Frozen meaning:

```text
LB → previous tab
RB → next tab
```

Do not reuse LB/RB for page-specific controls while this Overlay navigation model is active.

Initial recommendation: do not wrap from the first tab to the last or the last to the first. At the boundary, the command is a no-op. This is simple and prevents accidental tab jumps. Hardware UX testing may revisit this later without changing the tab architecture.

### 7.2 LT / RT — intentionally empty

For the initial design:

```text
LT → no action
RT → no action
```

Do not assign a function merely because the inputs are available.

They remain reserved for a future proven use such as a coarse-step action or page-specific capability, but there is no requirement to use them.

### 7.3 B — always close Overlay

`B` is not a page-local Back button in this design.

Frozen rule:

```text
B
→ request Overlay close
→ OQ4 unified retirement/release gate
→ same virtual presentation resumes only after safe release
```

Pages must not consume B for another operation.

This intentionally avoids building an Overlay navigation stack just to support nested Back behavior.

If a future feature requires nested modal UI, its interaction design must respect the global B-close contract or explicitly revise this product decision first.

OQ5 should therefore prefer a semantic `CloseOverlay` action over allowing each page to interpret `Back` independently.

### 7.4 A — Select / Activate

`A` is the standard activation action.

Examples:

```text
Toggle row selected
+ A
→ toggle state

Button/action row selected
+ A
→ execute action

Choice/dropdown row selected
+ A
→ open/select when that control type is introduced

Shortcut tile selected
+ A
→ run the assigned shortcut
```

A slider does **not** require A to enter an edit mode.

### 7.5 X / Y — intentionally unused initially

`X` and `Y` remain reserved/no-op in the basic shell.

Do not invent secondary actions merely to fill the controller.

---

## 8. DPad and both analog sticks are navigation inputs

Frozen product direction:

```text
DPad
Left Stick
Right Stick
```

all support directional navigation.

The Overlay process still must not read raw controller devices itself.

The OQ4 architecture remains:

```text
PID1902 DirectInput
→ Runtime ControllerState
→ narrow semantic Overlay input router
→ low-rate Overlay navigation IPC
```

Analog stick support must therefore be added at the existing Runtime semantic-input seam, not by opening XInput/GameInput/DirectInput from `Overlay.exe`.

### 8.1 Direction semantics

Conceptually all three directional sources produce the same UI intentions:

```text
Up
Down
Left
Right
```

The visible page interprets the direction according to the currently selected element.

Examples:

```text
ordinary vertical row
Up/Down → move selection

slider row
Left/Right → adjust value

Shortcut 2×2 tile area
Up/Down/Left/Right → move between tiles
```

Do not create separate page behavior for “DPad Left” vs “Left Stick Left” vs “Right Stick Left”.

### 8.2 Analog stick threshold

Analog sticks should be converted to semantic digital direction using one narrow threshold/deadzone policy in the Runtime input router.

Do not send raw analog axis values over `.Overlay` just so XAML can decide whether the user meant navigation.

The exact threshold/repeat timing should be hardware-tuned later. Start simple; do not introduce gesture/velocity/acceleration frameworks.

### 8.3 Direction repeat is separate from mutation debounce

Held-direction navigation and feature-mutation debounce are different problems.

Do not use the 2-second slider commit delay as a navigation repeat delay.

If hardware testing shows that holding a stick/DPad should repeatedly move/adjust, add one narrow held-direction repeat policy. Do not create a generalized input timing manager before it is needed.

---

## 9. Logical selection model

The top-level Overlay window is intentionally no-activate/topmost, so the controller UX must not depend on ordinary activated keyboard focus.

Use a clear logical selected-item model.

Required properties:

- exactly one selected tab;
- within the selected tab, at most one selected actionable item;
- selected state is visually obvious;
- disabled/unavailable items cannot become active mutation targets;
- moving selection into a scrolled-off row brings the row into view;
- pointer/touch interaction may update logical selection where useful, but controller selection remains authoritative for controller navigation.

Do not build a generalized focus manager shared with the desktop UI.

A narrow Overlay-only selection model is sufficient.

### 9.1 Selection on Show

On every Show:

```text
selected tab = configured first tab
selected item = first usable/focusable item on that tab
```

If the page contains no usable item, keep the page visible with an empty/unavailable state and no selected action row.

### 9.2 Selection on tab change

Initial simple policy:

```text
LB/RB changes tab
→ select the first usable item in the destination tab
```

Do not persist per-tab item positions across Overlay closes.

Retaining per-tab position for the current visible session can be considered later if hardware UX proves it materially better, but it is not required for the first shell.

---

## 10. Slider interaction contract

Slider behavior is a primary handheld UX rule.

Do **not** require:

```text
select slider
→ press A to enter edit mode
→ press Left/Right
→ press A again to exit
```

Required behavior:

```text
Up/Down
→ move selection to slider row

Left/Right while slider row is selected
→ immediately adjust displayed value
```

A slider is therefore both:

- one vertical navigation item;
- an immediate horizontal adjustment target.

This applies equally to DPad and either analog stick after they are converted to semantic direction.

### 10.1 Step size

Use the feature's existing semantic step.

Examples from current product behavior may include:

- TDP: feature-defined watt step;
- FPS limit: feature-defined discrete values/step;
- Power/CPU mode: discrete enum indices.

Do not invent a generic fractional step system in the Overlay.

### 10.2 Immediate visual preview, delayed authoritative commit

Repeated left/right adjustments should update the visible draft immediately so the UI feels responsive, while actual Runtime mutation may remain deliberately relaxed/debounced as described below.

---

## 11. Toggle interaction contract

Default toggle behavior:

```text
Up/Down
→ select toggle row

A
→ toggle On / Off
```

Left/Right does not need to duplicate A for the initial design.

A toggle mutation should show the authoritative result/readback from Runtime. Do not permanently trust a local visual flip if the hardware/persistence mutation fails.

---

## 12. Other common row types

The initial UI should remain row-oriented rather than becoming a grid of unrelated desktop cards.

Useful conceptual row types:

### 12.1 Slider row

```text
Label                              Current Value
━━━━━━━━━━━━━━━━●━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

- entire row is one logical navigation target;
- Left/Right adjusts;
- value is always visible;
- slider can occupy the width below the label/value line.

### 12.2 Toggle row

```text
CPU Boost                              [ Toggle ]
```

- one navigation target;
- A toggles.

### 12.3 Choice row

```text
Power Mode                         Balanced  ›
```

- A opens/selects when needed;
- initial implementation may use a compact flyout/list or an alternate row interaction if hardware testing prefers it.

Do not freeze a complicated combobox architecture before the actual feature is implemented.

### 12.4 Action row

```text
Apply / Reset / Open / Run action
```

- A executes;
- operation result comes from Runtime.

### 12.5 Information row

Read-only state such as device/profile identity may be displayed but should not become a controller selection target unless it has an action.

### 12.6 Empty/unavailable state

Examples:

```text
No active game profile
Controller option unavailable on this board
Feature not configured
```

The UI should explain unavailability rather than leaving a broken-looking blank page.

---

## 13. Reuse the current Steam QAM mutation/debounce policy

The current Steam QAM intentionally uses relaxed mutation timing for slider-like controls.

Current `qam.js` defines:

```text
QAM_SLIDER_COMMIT_DELAY_MS = 2000
```

and `scheduleQamSliderCommit(...)` replaces the pending timer for the same logical mutation key.

The desired Overlay behavior should preserve this product policy rather than making hardware writes happen for every rapid controller step.

### 13.1 Slider-like mutation semantics

Required behavior:

```text
Left/Right changes value
→ update local visible draft immediately
→ schedule authoritative mutation

another Left/Right before 2 seconds
→ replace/reset pending commit for the same key
→ keep latest draft visible

2 seconds after last change
→ send latest desired value/configuration to Runtime
→ receive authoritative result/readback
→ reconcile visible state
```

This is appropriate because these Quick Settings do not need every intermediate value to become a hardware write.

### 13.2 Preserve pending preview across state invalidation

The current QAM deliberately avoids overwriting a user's pending slider draft with stale/earlier authoritative refresh state while a commit is pending.

The Overlay should preserve the same behavior:

```text
pending local slider draft exists
+ Runtime state invalidation arrives
→ do not visibly jump the control back to the older value
→ preserve pending draft
→ settle against authoritative readback after commit
```

### 13.3 Toggles remain immediate

The existing QAM generally treats toggle actions as immediate mutations rather than applying the 2-second slider debounce to them.

Initial Overlay rule:

```text
A toggles
→ issue mutation immediately
→ show pending/busy state only as needed
→ apply authoritative result/readback
```

Do not delay a simple On/Off toggle for 2 seconds merely because sliders use delayed commit.

### 13.4 Reuse behavior, not necessarily code abstraction

QAM is JavaScript/React inside Steam; Addon Overlay is C#/WinUI 3.

Do **not** refactor the QAM implementation into a cross-language/generalized debounce subsystem merely to claim code reuse.

Reuse:

- the 2-second relaxed commit policy;
- latest-value-wins per mutation key;
- immediate local preview;
- authoritative result/readback;
- pending-draft protection across invalidation.

Implement it at the narrowest appropriate OQ5 seam.

---

## 14. Shortcut page contract

Shortcut is intentionally different from ordinary setting rows.

The long-term UI reserves four fixed quick-action slots.

Preferred shape:

```text
┌─────────────────┐  ┌─────────────────┐
│ Shortcut 1      │  │ Shortcut 2      │
│ action label    │  │ action label    │
└─────────────────┘  └─────────────────┘

┌─────────────────┐  ┌─────────────────┐
│ Shortcut 3      │  │ Shortcut 4      │
│ action label    │  │ action label    │
└─────────────────┘  └─────────────────┘
```

A 2×2 tile layout is the preferred initial concept because it gives four large controller/touch targets without turning the page into a long list.

Controller behavior:

```text
DPad / either stick
→ move among the four slots

A
→ execute assigned action

LB/RB
→ leave Shortcut tab

B
→ close Overlay
```

### 14.1 Shortcut order/assignment is configurable

This page is the intentional exception to the general no-reorder rule.

Think of it as four fixed slots:

```text
Slot 1 = Action A
Slot 2 = Action B
Slot 3 = Action C
Slot 4 = Action D
```

The user may change which action occupies each slot and therefore their effective order/placement.

Do not build a generic reorderable-items system for the whole Overlay.

### 14.2 Shortcut feature is future work

The five-tab shell should include the Shortcut page now, but actual shortcut actions/configuration may be implemented later.

A placeholder/empty state is acceptable during the shell phase.

Do not block the basic tab/navigation work on selecting every future shortcut action.

---

## 15. General item ordering policy

Outside the two explicit customization surfaces below, ordinary feature rows remain product-defined.

User-configurable ordering:

```text
Top-level tabs
Shortcut slots (four fixed positions)
```

Not currently user-configurable:

```text
Device setting row order
Profile setting row order
Controller setting row order
Setting page row order
individual sections/cards
```

This keeps feature layout coherent and avoids:

- drag/reorder infrastructure;
- controller-specific reorder UX;
- item-order migration complexity;
- layout persistence for every row;
- generalized dashboard abstractions.

If future product evidence shows a strong need for row customization, treat it as separate UX work.

---

## 16. Base visual layout

The Overlay remains a left-aligned full-WorkArea-height panel.

Current window baseline:

```text
Panel width = 400 DIP
Reference display = 1920 × 1200
Reference scale = 150%
Physical width at 150% = 600 px
Height = current monitor WorkArea height
Background = #FFF3F3F3
```

This document does not change the existing WorkArea/DPI/window-placement architecture.

### 16.1 Current POC padding fact

Current `OverlayWindow.xaml` already contains:

```xml
<Grid x:Name="OpaquePanel" Background="#FFF3F3F3" Padding="28">
```

So the implementation does not literally have zero content padding today.

However, the current POC is only a single `StackPanel`, so it does not yet have a real product layout system for:

- header;
- tabs;
- scrollable body;
- sections/rows;
- fixed footer/hints.

OQ5 should turn the existing simple padding into explicit layout regions rather than just adding more elements to the same StackPanel.

### 16.2 Recommended shell regions

Conceptually:

```text
Root / OpaquePanel
├─ Header region
├─ Tab strip region
├─ Divider / spacing
├─ Page body region (scrollable)
└─ Footer/hint region (optional, fixed)
```

The page body should scroll independently.

The header and tab strip should remain visible while scrolling through a long Device/Profile page.

### 16.3 Content inset

Use the current approximately 28-DIP body inset as the first hardware baseline rather than inventing a dense edge-to-edge layout.

The tab strip may use a smaller horizontal inset than body content if five tabs need the width.

This is a layout decision, not a request to make all WinUI controls smaller.

### 16.4 Vertical rhythm

Use a consistent hierarchy:

```text
Header
↓
Tab strip
↓
Page/section heading
↓
Rows
↓
Section gap
↓
Next section
```

Avoid random per-control margins.

Prefer a small set of shared XAML resources for spacing once the first real page proves the values.

Do not create a design-token framework disconnected from WinUI resources.

---

## 17. Typography baseline

For the first UI pass, **do not custom-shrink typography**.

Use normal WinUI 3 typography/resources and the existing POC hierarchy as the starting point.

Current POC uses:

```text
Addon Quick Settings title = 28 DIP FontSize / SemiBold
secondary POC label         = 18 DIP
```

The production shell should move toward semantic typography roles rather than scattering arbitrary `FontSize` values:

- panel title;
- tab label;
- section title;
- row label;
- current value/secondary text;
- caption/footer hint.

Prefer existing WinUI text styles/resources where they produce acceptable hardware results.

Exact final point sizes are intentionally **not frozen yet**.

Reason:

- the 400-DIP panel is already established;
- five tabs need real-device validation;
- content density will become clearer when first real controls land;
- shrinking fonts prematurely can hurt 1920×1200 handheld readability.

---

## 18. WinUI control sizing baseline

For the first production UI shell and first feature controls:

> **Use standard WinUI 3 control sizing/templates.**

Do not immediately create compact custom templates for:

- `ToggleSwitch`;
- `Slider`;
- buttons;
- combo/select controls.

Do not use `ScaleTransform` to visually shrink standard controls while leaving confusing hit-test geometry.

First validate the standard controls inside the actual 400-DIP handheld panel.

If later hardware evidence shows that WinUI default toggles/sliders consume too much space, do a focused compact-style pass that adjusts the relevant template metrics deliberately.

That later polish should not require changing controller semantics or page architecture.

---

## 19. Selection/highlight visual contract

Because controller navigation is logical/no-activate, selection must be visually stronger than ordinary pointer hover.

Required distinction:

```text
Selected
Hovered
Pressed
Disabled
```

must not be visually ambiguous.

Initial direction:

- selected tab: clear underline/accent indicator;
- selected row: subtle but unmistakable row-level highlight/accent treatment;
- selected Shortcut tile: clear border/background state;
- disabled row: visibly unavailable and not actionable;
- current value remains readable while selected.

Exact color/rounding is intentionally not frozen in this document.

Use a small number of WinUI/theme resources rather than hard-coding a unique color into every row.

The existing light surface `#FFF3F3F3` remains the baseline until a later visual-design pass changes it intentionally.

---

## 20. Scroll behavior

Device/Profile/Controller/Setting pages may eventually contain more rows than fit vertically.

Use one vertical scroll region for page content.

Controller selection must automatically bring the newly selected item into view.

Do not require the user to separately focus a scrollbar.

Pointer wheel/touch scrolling may continue to work normally.

Do not place the top tab strip inside the scrolling body.

Shortcut's 2×2 four-slot baseline should normally fit without scrolling.

---

## 21. Pointer and touch behavior

Controller is the primary interaction, but pointer/touch remains first-class enough for handheld touchscreen use.

Inside Overlay:

- tabs are clickable/tappable;
- sliders/toggles/buttons remain normal WinUI interactive controls;
- selecting/tapping an item may update the logical selected item;
- touch must not require controller focus first.

Outside Overlay:

```text
outside pointer/touch down
→ existing dismiss signal
→ Runtime unified Overlay retirement
```

Do not add a full-screen transparent input catcher.

---

## 22. Page content and feature authority

Every page displays Runtime-owned state and requests Runtime-owned mutations.

Examples:

```text
Device / TDP slider
→ Overlay preview/debounce
→ Runtime TdpRuntime mutation
→ authoritative result/readback
```

```text
Profile / FPS
→ active profile fact from Runtime
→ profile mutation through existing profile authority
```

```text
Controller / future vibration
→ existing/future controller-device authority
```

The Overlay must not directly:

- write EC values;
- write registry/power settings independently;
- persist profile files;
- mutate HidHide;
- mutate PID1901/PID1902;
- attach/detach VIIPER devices.

---

## 23. Snapshot / refresh behavior

Preserve the existing Overlay architecture model:

```text
Overlay Show
→ fresh aggregate snapshot

user mutation
→ local preview when appropriate
→ Runtime mutation
→ authoritative result/readback

Runtime state invalidation
→ low-rate targeted refresh
```

Do not add a UI polling loop for ordinary Quick Settings values.

This is not ClawHUD telemetry.

The initial tab should not display stale values merely because the process stayed warm while hidden; every Show must refresh relevant state.

---

## 24. Global close behavior and pending mutations

B/outside click/toggle-off/Main UI open/Overlay process loss remain lifecycle events governed by OQ4.

The UI should stop accepting new mutations as close begins.

For debounced slider drafts, the focused OQ5 implementation must choose one simple explicit close policy and test it. Preferred direction:

```text
close begins
→ stop new adjustments
→ if latest draft has already been submitted, settle normally
→ do not keep Overlay capture alive solely to finish a long UI-side debounce timer
```

Do not let a 2-second UI debounce become a controller-safety/lifecycle authority.

Exact flush/cancel behavior for an unsubmitted draft should be frozen in the feature-binding work order after the transport mutation seam is implemented.

---

## 25. OQ4 capture remains the safety boundary

UI navigation must never bypass OQ4.

While Overlay is active:

```text
current X360 / SteamDeck stays selected/attached
publisher is neutral/paused
controller semantic input goes to Overlay
```

On close:

```text
stop navigation
→ hide/retire surface
→ wait consumed controls release
→ clear capture
→ resume/reconcile current presentation safely
```

Adding tabs, sticks, shortcuts, or settings controls must not introduce:

- a second DirectInput reader;
- a third virtual presentation;
- presentation switching just to navigate UI;
- page-specific controller publication gates;
- a second Overlay capture owner.

---

## 26. Sleep/resume, device loss, Runtime restart

The UI remains subordinate to Full PID1902 lifecycle.

### Suspend

If Overlay is visible:

```text
retire/hide Overlay interaction
→ Full PID1902 suspend lifecycle continues
```

Do not reopen the previous tab automatically after resume.

### Physical input loss

```text
PID1902 / DirectInput lost
→ OQ4 retires/cancels capture safely
→ existing physical recovery owns device return
→ Overlay UI does not reacquire controller
```

### Runtime restart/shutdown

```text
Runtime teardown
→ Overlay input/mutations stop
→ Overlay helper closes/disconnects
→ controller teardown remains Runtime-owned
```

Tab order/preferences may persist normally, but selected tab/item state does not become a recovery authority.

---

## 27. Explicit non-goals

Do not add as part of the basic UI architecture:

- OQ3-B Steam QAM visibility management;
- WING/OEM1 physical button mapping (OQ6);
- LT/RT functionality;
- X/Y functionality;
- user-created tabs;
- ordinary row reordering;
- generalized dashboard layout customization;
- arbitrary widget system;
- plugin architecture;
- raw controller state IPC;
- analog cursor/mouse emulation;
- custom rendering engine;
- WPF;
- custom compact WinUI templates before hardware evidence;
- a new cross-feature debounce manager;
- a separate Overlay settings/hardware authority.

---

## 28. Recommended UI implementation sequence

This is planning guidance, not a work order.

### OQ5-A — Shell / tabs / controller UX foundation

Implement first:

- five horizontal tabs;
- fixed identities: Device / Profile / Controller / Shortcut / Setting;
- ordered-tab data model;
- default order;
- every Show resets to first configured tab;
- placeholder pages;
- logical selected item/highlight;
- LB/RB tab movement;
- LT/RT no-op;
- B global close;
- A activation;
- DPad + Left Stick + Right Stick directional semantic navigation;
- slider-row direct Left/Right interaction contract even if the first shell uses a placeholder control;
- structured header/tab/body/footer layout;
- standard WinUI sizing/typography;
- existing 400-DIP/WorkArea/DPI/window behavior unchanged.

Do not bind all production feature mutations in the same PR if it makes the shell review large.

### OQ5-B — First real Device controls

Good candidates:

- TDP;
- CPU Boost;
- Windows Power Mode;
- Intel FPS Limit when product policy exposes it.

Reuse current Runtime feature authorities and current QAM mutation/debounce semantics.

### OQ5-C — Profile / Controller content and state polish

Add selected high-value controls only after the shell and Device interaction are hardware-proven.

Include:

- empty/unavailable states;
- authoritative mutation failure display;
- pending/draft behavior;
- selection/scroll polish.

### Future Shortcut implementation

Populate the already-existing Shortcut tab with four configurable quick-action slots.

Do not retrofit Shortcut as a new navigation architecture later; the tab/page shell already exists.

### Later visual polish

Only after real content is present:

- compact Toggle/Slider style if actually needed;
- exact typography tuning;
- final tab spacing;
- accent/highlight polish;
- content density adjustments.

---

## 29. Acceptance criteria for the UI foundation

Before calling the basic UI shell stable, hardware testing should prove at minimum:

### Structure

- all five tabs render at the target 1920×1200 / 150% reference configuration;
- tabs remain readable at the 400-DIP panel baseline;
- page body scroll does not move the tab strip;
- content has intentional insets and section spacing;
- no unintended clipping of standard WinUI controls.

### Startup-tab policy

- default order opens Device;
- reordered first tab opens first on every Show;
- moving to another tab then closing does not change the next startup tab;
- hidden warm process does not accidentally restore last selected tab.

### Controller navigation

- LB/RB changes tabs only;
- LT/RT do nothing;
- B always closes Overlay;
- A activates the selected actionable item;
- DPad navigates;
- Left Stick navigates;
- Right Stick navigates;
- already-held inputs at capture start do not produce accidental actions under the OQ4 edge/release policy.

### Slider UX

- select slider with Up/Down;
- Left/Right changes its preview immediately without pressing A;
- repeated changes preserve latest preview;
- eventual mutation follows the relaxed QAM-style commit policy;
- authoritative failure/readback can correct the displayed value.

### OQ4 safety

- game/Steam QAM behind Overlay receives no controller navigation while capture is active;
- B close does not leak B into the game;
- slider Left/Right does not leak into the game;
- outside touch dismisses Overlay and underlying surface remains usable afterward;
- no PID/DirectInput/HidHide/VIIPER presentation churn occurs due to tab/UI navigation.

---

## 30. Frozen baseline summary

```text
PANEL
- left-side Addon-owned WinUI 3 Overlay
- current 400 DIP width baseline
- WorkArea height
- #FFF3F3F3 light surface baseline
- standard WinUI control sizes initially
- no compact custom templates yet

TABS
- Device
- Profile
- Controller
- Shortcut
- Setting
- horizontal
- order is user-configurable
- first tab in order = startup tab
- no separate default-tab setting
- no last-tab restore

GLOBAL INPUT
- LB = previous tab
- RB = next tab
- LT = reserved / no-op
- RT = reserved / no-op
- B = always close Overlay
- A = select / activate
- X/Y = reserved / no-op
- DPad = direction
- Left Stick = direction
- Right Stick = direction

SLIDER
- Up/Down selects row
- Left/Right adjusts immediately
- A not required for edit mode
- immediate local preview
- QAM-style 2-second latest-value commit policy

TOGGLE
- Up/Down selects row
- A toggles
- immediate authoritative mutation/readback

CUSTOMIZATION
- top-level tab order: supported
- Shortcut: four fixed configurable slots
- ordinary Device/Profile/Controller/Setting row reorder: not supported

AUTHORITY
- Overlay is UI only
- Runtime owns feature state/mutations/persistence
- OQ4 owns capture / neutral publication / safe release
- no Steam-QAM close manager
- no physical-button policy in OQ5
```

This is the baseline that future OQ5 work orders should preserve unless hardware testing demonstrates a concrete usability problem.