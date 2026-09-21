# Work Order — Overlay UI Foundation PR-B: Controller-First Toggle / Value Row Primitives

> **Status:** Ready for implementation  
> **Prepared:** 2026-09-21  
> **Target repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main baseline:** `6a21a6aeb89d5934d05f998a799f69938cc1fdd7` (`refactor: split OverlayWindow responsibilities (#560)`)  
> **Architecture authority:** standalone Full PID1902  
> **Scope:** replace the Overlay's Slider-shaped presentation with the controller-first ValueRow interaction while preserving existing Runtime/shared Quick Settings authority, mutation timing, Device/Profile bindings, and controller semantic input. Keep Toggle as the boolean primitive. No new product feature is added in this PR.

---

## 1. Goal

Build the first real controller-first row foundation for the new large Overlay.

The current Overlay already has the correct input authority:

```text
Runtime controller capture
→ semantic Up / Down / Left / Right / Accept
→ .Overlay transport
→ OverlayWindow logical row selection
```

The remaining mismatch is visual/interaction presentation.

Current Quick Settings rows still render numeric/discrete values as a traditional WinUI `Slider`:

```text
TDP                         25 W
━━━━━━━━━━━━━━━━●━━━━━━━━━━━━━━
```

The new handheld Overlay should instead use one compact ordered-value interaction for both numeric and discrete/enum values:

```text
TDP                         ‹ 25 W ›
Power Mode             ‹ Balanced ›
CPU Boost Mode        ‹ Aggressive ›
```

Controller behavior:

```text
Up / Down
→ move row selection

Left
→ previous / lower value

Right
→ next / higher value

A
→ no edit mode for ValueRow
```

Touch/pointer behavior:

```text
left arrow button
→ previous / lower value

right arrow button
→ next / higher value
```

Boolean values remain a Toggle row:

```text
CPU Boost                        [●]
```

Controller:

```text
A
→ toggle
```

Pointer/touch:

```text
ToggleSwitch
→ direct native WinUI interaction
```

This PR does **not** connect any new feature.

Existing Device/Profile Quick Settings are used only as the real production consumer that proves the new primitives and the existing mutation/readback path.

---

## 2. Product decision frozen for this PR

The new Overlay is not a desktop settings form.

For common inline settings, the primary controller interaction language is:

```text
Boolean
→ ToggleRow
→ A

Numeric / discrete / enum-like ordered choice
→ ValueRow
→ Left / Right
```

Do not introduce a Slider edit mode.

Do not introduce a ComboBox/dropdown for current numeric/discrete Quick Settings.

Do not require `A` before Left/Right adjustment.

Do not add a bottom controller-hint strip.

Do not add a two-column interactive settings layout.

The current large Overlay width remains unchanged in this PR. Width/content-rail tuning will be decided after later hardware UI evaluation.

---


## 2.1 Surface scope — Overlay only

This PR changes **only** the Addon-owned WinUI Overlay renderer under:

```text
src/SteamInputAddonforClaw.Overlay/
```

It must **not** change the desktop WinUI application under:

```text
src/SteamInputAddonforClaw.UI/
```

Desktop UI controls remain exactly as they are today.

In particular, do **not** remove or replace desktop:

- `Slider` controls;
- `ComboBox` / dropdown controls;
- `ToggleSwitch` controls;
- SettingsCard/form-style layouts;
- desktop page interaction patterns.

The product intentionally has different presentation rules:

```text
Desktop UI
→ mouse/keyboard-oriented configuration surface
→ native Slider / ComboBox / SettingsCard patterns remain valid

Overlay UI
→ controller-first handheld surface
→ ordered numeric/discrete settings render as ValueRow
```

Do not attempt to create one shared visual control implementation for Desktop UI and Overlay.

They may share product/runtime state and mutation contracts, but they should remain separate presentation layers.

## 3. Why PR-B is intentionally narrower than the earlier conceptual primitive list

Earlier Foundation discussion considered:

```text
ToggleRow
ValueRow
ActionRow
StatusRow
ShortcutTile
```

Current source inspection shows that **only Toggle and ordered-value rows have real generic production consumers today**:

```text
QuickSettingsControlKind.Toggle
QuickSettingsControlKind.Slider

QuickSettingsSliderKind.Numeric
QuickSettingsSliderKind.Discrete
```

There is currently no generic Action or Status row contract consumed by Device/Profile.

Therefore PR-B should implement only the primitives that can be exercised immediately:

```text
OverlayToggleRow
OverlayValueRow
```

Do **not** add unused `OverlayActionRow`, `OverlayStatusRow`, generic row interfaces, or a primitive registry merely because later pages may need them.

Those primitives may be added in the next Foundation shell/information-architecture PR when an actual page requires them.

This keeps the Foundation aligned with the project's no-overengineering policy.

---

## 4. Mandatory references before editing

Read these documents together:

### Full PID1902 authority

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`

### Overlay architecture / current Foundation

5. `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
6. `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
7. `docs/work-order/OVERLAY_UI_FOUNDATION_PR_A_STRUCTURAL_REFACTOR_WORK_ORDER.md`
8. `docs/work-order/OVERLAY_LARGE_FLOATING_SURFACE_GEOMETRY_THEME_POC_WORK_ORDER.md`

### Current source

Inspect current main before editing:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlaySliderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayQuickSettingsPageBinding.cs
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
```

Inspect relevant tests:

```text
tests/SteamInputAddonforClaw.Tests/OverlaySliderRowTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayToggleRowTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayRowSelectionTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayDelayedSliderCommitTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.Tests/AddonQuickSettingsSurfaceParityTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
```

If `main` has advanced beyond the reviewed SHA, adapt this work order to the current code instead of mechanically restoring the reviewed snapshot.

---

## 5. Current source facts

### 5.1 PR-A successfully separated responsibilities

Current `OverlayWindow` is now split into:

```text
OverlayWindow.xaml.cs
OverlayWindow.Presentation.cs
OverlayWindow.Shell.cs
OverlayWindow.Navigation.cs
OverlayWindow.QuickSettings.cs
```

Keep this ownership split.

PR-B should be concentrated in:

```text
OverlayValueRow
OverlayToggleRow
OverlayWindow.QuickSettings
tests
```

Do not move row rendering back into the main `OverlayWindow.xaml.cs`.

### 5.2 `OverlayWindow.xaml` is currently small

The main Overlay XAML currently contains only the large-surface shell:

```text
AnimationViewport
→ OpaquePanel
→ AnimatedContent
→ TabStrip + BodyScroll + TabBody
```

It is not currently a large monolithic XAML file.

Therefore do **not** use PR-B to introduce a new XAML component framework or migrate the row primitives to `UserControl` solely for theoretical future maintainability.

The existing dedicated row source files are already a clean visual ownership boundary.

If a future row becomes visually complex enough to justify a XAML control, migrate that concrete primitive then.

### 5.3 Current Slider presentation is the part that no longer matches the product direction

`OverlaySliderRow` currently creates:

```text
label + visible value
+
full-width WinUI Slider
```

and pointer/touch can drag the Slider arbitrarily.

The pure `OverlaySliderModel` already contains useful state logic:

- availability;
- min/max validation;
- step validation;
- clamp;
- snap-to-step;
- current local preview;
- duplicate-request suppression;
- controller step adjustment.

Preserve those useful rules, but make them value-row semantics rather than Slider-control semantics.

### 5.4 Current discrete options already map cleanly to ordered values

`OverlayWindow.QuickSettings.cs` currently maps a discrete product contract into Slider indices:

```text
QuickSettingsSliderKind.Discrete
options[]
current option value
→ current index
→ range 0 .. options.Count - 1
→ step 1
```

This is exactly the correct foundation for a controller-first ValueRow.

No new enum/dropdown transport contract is necessary.

### 5.5 Mutation timing does not belong to the row visual

Current delayed/grouped commit policy is already owned by:

```text
OverlayQuickSettingsPageBinding
OverlayDelayedSliderCommit
QuickSettingsCommitPolicy
```

Keep that architecture.

ValueRow emits desired ordered-value changes through the same callback seam.

Do not move debounce/commit policy into `OverlayValueRow`.

---

## 6. Keep the shared Quick Settings product contract unchanged

This is a presentation migration in the Overlay only.

Do **not** rename or redesign shared contract terms merely because the Overlay no longer draws a Slider.

Keep:

```csharp
QuickSettingsControlKind.Slider
QuickSettingsSliderKind.Numeric
QuickSettingsSliderKind.Discrete
QuickSettingsSliderSpec
QuickSettingsLinkedSliderConstraint
QuickSettingsCommitPolicy
QuickSettingsCommitGroupId
```

These are shared product semantics used by QAM and Runtime/shared projection.

Conceptually:

```text
shared contract "Slider"
→ means an ordered adjustable numeric/discrete setting

QAM renderer
→ may continue to render a native Slider

Overlay renderer
→ renders the same contract as ValueRow
```

Do not force both frontends to share the same visual control.

No shared-contract or transport protocol version bump should be required.

---

# 7. Replace `OverlaySliderRow` with `OverlayValueRow`

## 7.1 File/type migration

Replace:

```text
src/SteamInputAddonforClaw.Overlay/OverlaySliderRow.cs

OverlaySliderModel
OverlaySliderRow
```

with:

```text
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs

OverlayValueModel
OverlayValueRow
```

Delete the old `OverlaySliderRow` implementation when all Overlay references have migrated.

Do not keep both visual primitives alive for compatibility. The Overlay is pre-release and the old Slider presentation is intentionally being replaced.

### Important boundary

Do **not** rename:

```text
OverlayDelayedSliderCommit
QuickSettingsSliderKind
QuickSettingsSliderSpec
```

Those names belong to existing mutation/product contracts, not the visual primitive.

---

## 7.2 `OverlayValueModel`

The pure model should own only ordered-value state and desired-step requests.

Recommended state:

```csharp
bool ConstraintsValid
bool IsAvailable
double Minimum
double Maximum
double Step
double PreviewValue
bool CanDecrease
bool CanIncrease
```

Recommended external operations:

```csharp
ApplyState(
    bool isAvailable,
    double minimum,
    double maximum,
    double step,
    double value)

RequestAdjust(int delta)
```

`delta` remains the existing semantic:

```text
-1 = previous / lower
+1 = next / higher
```

Preserve current normalization:

```text
clamp
→ snap to semantic step relative to Minimum
→ clamp again
→ bounded floating-point cleanup
```

Preserve:

- malformed ranges fail closed;
- unavailable rows emit no request;
- boundary adjustment emits no duplicate request;
- local preview advances immediately;
- authoritative `ApplyState` replaces preview without emitting another request.

### Remove arbitrary pointer-set semantics

The new ValueRow does not expose a draggable continuous Slider.

Therefore the old visual-path API:

```csharp
RequestSet(double desired)
```

is no longer required by the row primitive.

Prefer removing it rather than carrying an unused Slider-era API.

Pointer/touch should use the same one-step `RequestAdjust(-1/+1)` seam as controller input.

This deliberately makes pointer and controller adjustment semantics consistent.

---

# 8. `OverlayValueRow` visual / interaction

## 8.1 Layout

Use one compact row.

Conceptually:

```text
┌────────────────────────────────────────────┐
│ Label                         ‹ Value ›     │
└────────────────────────────────────────────┘
```

Implementation can remain a dedicated C# WinUI row object like the existing row primitives.

Suggested structure:

```text
Border Container
└─ Grid
   ├─ label TextBlock
   └─ horizontal value controls
      ├─ previous Button
      ├─ value TextBlock
      └─ next Button
```

Do not render a `Slider`.

Do not render a `ComboBox`.

Do not open a Flyout for current numeric/discrete settings.

## 8.2 Pointer/touch targets

The arrow glyph itself may stay visually compact, but each arrow button needs a practical touch target.

Use a minimum button target around:

```text
40 DIP
```

or the nearest normal WinUI sizing that provides equivalent touch usability.

Do not create a custom touch gesture recognizer.

Use normal WinUI `Button.Click`.

## 8.3 Boundary state

At the minimum:

```text
previous button disabled
```

At the maximum:

```text
next button disabled
```

Unavailable row:

```text
both buttons disabled
row is not controller-selectable
```

The visible value should still show the latest authoritative/preview text when valid.

Malformed state should continue to fail closed and may display:

```text
--
```

as today.

## 8.4 Controller capability

`OverlayValueRow.Capabilities` should remain:

```csharp
IsSelectable: () => model.IsAvailable
Activate: null
Adjust: OnControllerAdjust
```

No ValueRow edit mode.

No A behavior.

No WinUI focus engagement.

## 8.5 Value formatting

Preserve the existing formatter seam.

Examples:

```text
numeric:
25 + " W" → "25 W"

discrete:
index → options[index].Label
```

Do not move product labels/options into Overlay-specific tables.

The shared Quick Settings snapshot remains the product source.

---

# 9. ToggleRow stays the boolean primitive

Keep the current `OverlayToggleModel` behavior:

```text
authoritative ApplyState
A → request opposite
pointer ToggleSwitch → request desired value
unavailable → no mutation
readback → no feedback loop
```

Do not replace the native WinUI `ToggleSwitch` with a custom-drawn switch.

PR-B may make **small visual alignment changes** so ToggleRow and ValueRow read as members of the same row family:

- comparable vertical padding;
- comparable label alignment;
- comparable corner radius;
- comparable row height.

Do not use this PR for final visual polish.

The next Foundation shell/integration PR will own selection visual, section rhythm, and broader hierarchy.

---

# 10. Quick Settings renderer migration

Update `OverlayWindow.QuickSettings.cs` so the Overlay renderer stores and renders Value rows instead of Slider rows.

Recommended surface state migration:

```csharp
Dictionary<QuickSettingsRowId, OverlaySliderRow> SliderRows
```

→

```csharp
Dictionary<QuickSettingsRowId, OverlayValueRow> ValueRows
```

Update the renderer naming to reflect Overlay presentation where practical.

Recommended examples:

```text
ApplyQuickSettingsSliderState
→ ApplyQuickSettingsValueState

CreateQuickSettingsSliderRow
→ CreateQuickSettingsValueRow
```

`QuickSettingsRowShape` may still contain `QuickSettingsSliderKind?` because that is shared contract shape used to determine renderer compatibility.

Do not rename shared Slider metadata simply to make local names match.

---

## 10.1 Numeric rows

Current numeric path:

```text
QuickSettingsSliderKind.Numeric
→ min / max / step / current integer
→ suffix formatter
```

Target:

```text
QuickSettingsSliderKind.Numeric
→ OverlayValueRow
→ Left/Right one semantic step
→ same ScheduleQuickSettingsSlider(...) path
```

Keep the existing local-preview and trailing/grouped commit behavior.

Example:

```text
Plugged in · PL1                  ‹ 25 W ›
```

---

## 10.2 Discrete rows

Current discrete path already maps current value to an ordered option index.

Target:

```text
QuickSettingsSliderKind.Discrete
→ OverlayValueRow
→ previous/next option by index
→ existing option's product Value submitted
```

Example:

```text
Power Mode                  ‹ Balanced ›
CPU Boost Mode           ‹ Aggressive ›
```

This is the current equivalent of a dropdown/enum selection in the Overlay.

Do not create a ComboBox.

Do not create a separate `ChoiceRow` merely because the product source is discrete.

One `OverlayValueRow` owns both numeric and discrete visual interaction.

---

# 11. Keep the current mutation/readback path

For both numeric and discrete values, preserve:

```text
controller/touch adjustment
→ ValueRow local preview changes immediately
→ request callback
→ OverlayWindow.QuickSettings
→ ScheduleQuickSettingsSlider(...)
→ OverlayQuickSettingsPageBinding
→ shared delayed/grouped commit policy
→ Runtime mutation
→ authoritative result/readback
→ ApplyState into existing ValueRow instance
```

Keep:

- latest-value-wins behavior;
- 2-second policy where the shared row contract specifies it;
- linked TDP group behavior;
- generation/stale-settlement guards;
- hide-time unsubmitted draft cancellation;
- local failure banner;
- Runtime authoritative result winning after settlement.

Do not add a second debounce helper for ValueRow.

---

# 12. Same-shape fast path must remain

Current generic renderer intentionally updates existing row instances when page shape and context are unchanged.

This previously protected an in-progress WinUI Slider drag, but it still has value after the Slider is removed:

- local preview stays on the same row instance;
- controller selection is not rebuilt;
- pointer/touch interaction is not torn down;
- scroll position stays stable;
- unnecessary WinUI tree churn is avoided.

Update stale comments that specifically describe preserving a Slider drag, but keep the fast-path architecture.

Preserve the existing guards:

```text
same RowShape
same AppId
same section ID/label/message
```

---

# 13. Selection authority remains unchanged

Keep:

```text
OverlayRowSelection
OverlayRowCapabilities
OverlayWindow.Navigation
```

as the one logical controller selection path.

ValueRow plugs into the existing:

```text
Adjust(-1/+1)
```

capability.

ToggleRow plugs into:

```text
Activate()
```

Do not add:

- separate Slider selection;
- separate enum selection;
- WinUI keyboard focus as controller authority;
- XYFocus graph;
- FocusEngagement;
- another navigation manager.

---

# 14. Touch-to-logical-selection synchronization is deferred

A pointer user may click a ValueRow arrow or ToggleSwitch directly.

In this PR, the primary requirement is that the correct setting changes.

Do not expand PR-B into the shell-level policy of:

```text
touching any row
→ make that row the logical controller selection
```

That requires a page-index/selection integration seam and belongs to Foundation PR-C with the unified shell/selection visual work.

PR-B should not add `TrySelect(index)` solely to support that future behavior.

---

# 15. No new XAML component framework in PR-B

Do not create:

```text
Controls/
  Base/
  Behaviors/
  Templates/
  Providers/
```

Do not introduce a generic `OverlayRowBase`.

Do not introduce an `IOverlayRow` hierarchy.

Do not migrate these two simple row primitives into XAML `UserControl` merely to create a new component system.

Current main already has each visual primitive in a dedicated source file, and `OverlayWindow.xaml` itself is small.

Prefer the smallest implementation that makes the interaction correct.

If later visual complexity justifies a XAML control, migrate that concrete row in a focused polish/refactor PR.

---

# 16. Explicitly do not add ActionRow / StatusRow yet

Do not create unused:

```text
OverlayActionRow
OverlayStatusRow
```

in PR-B.

The next Foundation PR will define the actual new shell/section structure and map real visible content. At that point, if a real action/status consumer exists, add the narrow primitive required by that page.

This avoids a future-looking primitive framework with no production consumer.

---

# 17. Files expected to change

Primary implementation:

```text
src/SteamInputAddonforClaw.Overlay/OverlaySliderRow.cs       delete
src/SteamInputAddonforClaw.Overlay/OverlayValueRow.cs        new
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs       small alignment changes only if needed
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
```

Primary tests:

```text
tests/SteamInputAddonforClaw.Tests/OverlaySliderRowTests.cs  delete/replace
tests/SteamInputAddonforClaw.Tests/OverlayValueRowTests.cs   new
tests/SteamInputAddonforClaw.Tests/OverlayToggleRowTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayDeviceRendererWiringTests.cs
```

Possible narrow source-contract adjustments:

```text
tests/SteamInputAddonforClaw.Tests/AddonQuickSettingsSurfaceParityTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
```

Expected unchanged:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayQuickSettingsPageBinding.cs
src/SteamInputAddonforClaw.Overlay/OverlayDelayedSliderCommit.cs
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/*
src/SteamInputAddonforClaw/Lifecycle/*
src/SteamInputAddonforClaw.QamHost/*
```

Do not touch Runtime/controller lifecycle code for this PR.

---

# 18. Unit tests — OverlayValueModel

Replace the current pure `OverlaySliderModel` tests with equivalent `OverlayValueModel` coverage.

Required cases:

### Valid state

```text
valid available state
→ ConstraintsValid = true
→ IsAvailable = true
→ initial value snaps to semantic step
→ no request emitted
```

### Authoritative range clamp

```text
authoritative value outside range
→ preview clamps/snap safely
```

### Malformed constraints

Cover:

- minimum > maximum;
- step <= 0;
- NaN;
- infinity.

Expected:

```text
fail closed
IsAvailable = false
no adjustment request
```

### Unavailable

```text
RequestAdjust
→ no request
```

### Controller/touch step behavior

Because both controller and arrow buttons now use the same model seam:

```text
RequestAdjust(+1)
RequestAdjust(-1)
```

must change exactly one semantic step.

### Boundaries

At min/max:

```text
CanDecrease / CanIncrease reflect the boundary
extra adjustment past boundary emits no duplicate callback
```

### Preview continuity

Repeated adjustments must continue from the current local preview, not wait for Runtime readback between every step.

### Authoritative replacement

```text
local preview changes
→ ApplyState(authoritative value)
→ preview becomes authoritative value
→ no callback emitted
```

Remove Slider-specific pointer-set tests that depend on arbitrary drag values.

---

# 19. Renderer/source-contract tests

Update source/composition tests to prove the new renderer architecture.

At minimum assert:

```text
OverlayWindow.QuickSettings uses OverlayValueRow
QuickSettingsSurface stores ValueRows, not SliderRows
numeric shared Slider rows map to OverlayValueRow
discrete shared Slider rows map to the same OverlayValueRow
OverlaySliderRow is no longer referenced by production Overlay renderer
shared QuickSettingsControlKind.Slider remains unchanged
shared QuickSettingsSliderKind Numeric/Discrete remain unchanged
ScheduleQuickSettingsSlider / binding commit authority remains unchanged
```

Do not weaken current tests protecting:

- Device/Profile one generic renderer;
- AppId/section-text fast-path guards;
- local failure banner;
- no duplicate product policy;
- one shared row-selection model;
- visibility filtering;
- malformed-row fail closed;
- QAM/Overlay shared product parity.

Update test names/comments where they incorrectly imply the Overlay still renders a WinUI Slider.

---

# 20. Build / automated validation

Run:

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx --no-restore -v:minimal
dotnet build SteamInputAddonforClaw.slnx --configuration Release --no-restore -v:minimal
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj
git diff --check
```

No new warnings.

Do not delete or skip tests merely because the visual primitive name changed.

---

# 21. Hardware/manual validation

This PR changes the actual interaction presentation, so perform a focused hardware pass on the supported MSI Claw.

## 21.1 Numeric ValueRow

Use an existing Device/Profile numeric setting such as TDP.

Verify:

```text
selected numeric row
→ DPad Left/Right changes one configured step

rapid repeated Left/Right
→ visible value previews immediately
→ existing trailing/grouped commit semantics still apply

minimum/maximum
→ controller cannot move outside range
→ corresponding pointer arrow is disabled
```

## 21.2 Discrete ValueRow

Use an existing discrete setting such as Power Mode / CPU Boost mode.

Verify:

```text
selected row
→ Left/Right cycles in the shared option order
→ displayed label comes from QuickSettingsDiscreteOption.Label
→ no dropdown/flyout opens
```

## 21.3 Touch/pointer

Verify:

```text
tap previous arrow
→ exactly one previous/lower step

tap next arrow
→ exactly one next/higher step
```

No draggable Slider should remain in Device/Profile Overlay Quick Settings.

## 21.4 Toggle

Verify existing toggle behavior remains:

```text
controller A
→ toggle

touch ToggleSwitch
→ toggle
```

No feedback loop after Runtime readback.

## 21.5 Lifecycle regression

Also verify:

- Show/Hide remains correct;
- B closes Overlay as before;
- LB/RB tabs remain correct;
- row Up/Down selection remains correct;
- outside-click dismissal remains correct;
- no controller input leaks after close;
- no geometry/topmost/no-activate regression.

---

# 22. Explicit non-goals

Do not implement:

- new Device feature;
- new Profile feature;
- Controller tab features;
- fan control;
- battery limit;
- LED;
- vibration-strength feature;
- M1/M2 mapping;
- new Shortcut actions;
- ActionRow;
- StatusRow;
- dropdown/ComboBox UI;
- modal selection surface;
- nested page/back stack;
- touch-to-logical-row selection sync;
- new selection visual language;
- section visual redesign;
- content-width/max-width policy;
- Overlay window width change;
- two-column interactive content;
- bottom controller hints;
- right-side status pane;
- outside-click behavior option;
- QAM removal;
- NavigationView migration;
- XYFocus/FocusEngagement;
- new controller input reader;
- shared Quick Settings contract rename;
- transport protocol change.

---

# 23. Review criteria

Treat these as blocking.

### Blocking

- a WinUI Slider remains in Overlay Device/Profile value rows;
- numeric and discrete settings use different controller interaction models without a real requirement;
- Left/Right requires an A/edit-mode transition;
- discrete choices open a dropdown/flyout;
- pointer arrows bypass the same normalized one-step model used by controller input;
- min/max boundaries emit duplicate/out-of-range requests;
- delayed/grouped commit behavior changes;
- authoritative readback no longer wins;
- Device/Profile generic renderer is duplicated;
- shared Quick Settings contracts are renamed/redesigned for Overlay-only presentation;
- QAM behavior is changed;
- controller/transport/lifecycle authority is changed;
- new generic row framework/manager/interface is added without a current consumer.

### Non-blocking

- exact arrow glyph choice (`‹ ›`, `◂ ▸`, or equivalent) if clear and touchable;
- small row padding/alignment tuning needed for visual consistency;
- exact private helper naming inside `OverlayValueRow`.

### Theoretical / do not block

Do not add locks/state machines/epochs for hypothetical pointer/controller interleavings.

The existing authoritative readback + pending commit/generation model remains the real correctness boundary.

---

# 24. Acceptance checklist

### Primitive

- [ ] `OverlaySliderRow` production primitive is removed.
- [ ] `OverlayValueModel` exists and preserves clamp/snap/preview semantics.
- [ ] `OverlayValueRow` renders label + previous/value/next, with no WinUI Slider.
- [ ] Numeric and discrete values use the same ValueRow.
- [ ] Boundary arrow buttons disable correctly.
- [ ] Unavailable ValueRow is non-selectable and non-mutating.
- [ ] ToggleRow remains the boolean primitive.

### Architecture

- [ ] Shared Quick Settings Slider contract remains unchanged.
- [ ] QAM remains unchanged.
- [ ] `OverlayQuickSettingsPageBinding` remains commit/readback authority.
- [ ] `OverlayRowSelection` remains selection authority.
- [ ] No new controller reader exists in Overlay.
- [ ] No Action/Status primitive is prebuilt without a consumer.
- [ ] No generic UI manager/framework is introduced.

### Regression

- [ ] Device Quick Settings still work.
- [ ] Profile Quick Settings still work.
- [ ] Numeric delayed/grouped commit behavior still works.
- [ ] Discrete option ordering/value mapping still works.
- [ ] Show/Hide/topmost/no-activate behavior is unchanged.
- [ ] Full test suite passes.
- [ ] Debug and Release builds pass.
- [ ] `git diff --check` passes.

---

# 25. Follow-up boundary — Foundation PR-C

After PR-B is merged and the controller/touch ValueRow interaction is hardware-validated, the next Foundation PR should integrate the final shell-level presentation rules:

```text
single-column Section → Row structure
selection visual hierarchy
row spacing/height
section headers
touch → logical row selection synchronization
Shortcut tile visual alignment
actual Action/Status primitives only where the target pages require them
```

That PR may change visible shell hierarchy.

PR-B should remain focused on the two common editable primitives and the existing generic Device/Profile renderer migration.
