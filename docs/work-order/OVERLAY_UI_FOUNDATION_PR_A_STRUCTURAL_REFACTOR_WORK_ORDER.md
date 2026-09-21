# Work Order — Overlay UI Foundation PR-A: OverlayWindow Structural Refactor

> **Status:** Ready for implementation  
> **Prepared:** 2026-09-21  
> **Target repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main baseline:** `7f7eb3ad12861cb9c17f877093c58d59d05033ac` (`feat: expand overlay into large floating surface (#559)`)  
> **Architecture authority:** standalone Full PID1902  
> **Scope:** behavior-preserving structural refactor of the existing WinUI 3 Overlay window before the new controller-first UI foundation is implemented. No new row primitive, feature binding, controller behavior, transport contract, geometry policy, or visual redesign in this PR.

---

## 1. Goal

Refactor the current `OverlayWindow` implementation so the next Overlay UI Foundation work can be implemented on a clear, maintainable code base.

Current `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs` is approximately **1,095 lines** and currently owns several independent UI responsibilities in one file:

```text
Overlay process/window presentation lifecycle
+ large-surface Show/Hide animation
+ DPI/native geometry diagnostics
+ top-level five-tab shell
+ persisted tab-order projection/editor
+ logical controller row selection/navigation
+ Shortcut 2x2 selection
+ generic Device/Profile Quick Settings rendering
+ generic Quick Settings mutation binding
+ row creation/value refresh
```

The current behavior is working, but continuing to add the new controller-first Overlay UI on top of this file would concentrate more unrelated UI responsibilities into one code-behind file.

This PR must therefore:

1. keep **one `OverlayWindow` owner**;
2. keep the existing XAML window and current product behavior;
3. split the existing code-behind into a small number of responsibility-based `partial` files;
4. preserve all existing fields/state authorities rather than wrapping them in new managers/services;
5. preserve all current public/internal method/event seams used by `App.xaml.cs`, tests, and transport;
6. keep all current visual behavior unchanged;
7. leave the code ready for Foundation PR-B to replace the current Slider-oriented row presentation with the new controller-first row primitives.

This is a structural refactor only.

The intended outcome is:

```text
same OverlayWindow
same Runtime/transport/input behavior
same visible UI
same tab/order/selection behavior
same Quick Settings behavior
same Show/Hide behavior

but

OverlayWindow responsibility is no longer concentrated in one ~1,095-line file
```

---

## 2. Why this PR comes first

The Overlay is still pre-release.

The project therefore does **not** need to preserve the current internal file layout merely because it exists.

However, the existing functional contracts are already useful and proven:

- Full1902 Runtime owns controller input/capture;
- Overlay receives semantic controller commands only;
- the Overlay process remains no-activate/topmost;
- PR #559 established the large floating surface geometry/presentation;
- five fixed tab identities and Runtime-owned tab ordering already work;
- logical row selection already works without HWND activation;
- Device/Profile generic Quick Settings transport/binding already works;
- Shortcut selection already has its own small fixed 2x2 model.

The next Foundation work will intentionally change the visual row language:

```text
current
Slider visual / Toggle visual

next
ValueRow / ToggleRow / ActionRow / StatusRow / ShortcutTile
```

Do **not** combine that behavior/visual migration with the structural refactor.

A behavior-preserving PR-A gives a clean baseline so later UI changes are easy to review and regressions are attributable to the correct PR.

---

## 3. Mandatory references before editing

Read these documents together before implementation.

### Full PID1902 authority

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`

### Overlay architecture / UI history

5. `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
6. `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
7. `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
8. `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`
9. `docs/work-order/OVERLAY_LARGE_FLOATING_SURFACE_GEOMETRY_THEME_POC_WORK_ORDER.md`

### Current source

Inspect current main before editing:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlaySliderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs
src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/WindowInterop.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayControllerInputRouter.cs
```

Inspect relevant existing tests before moving code.

If `main` has advanced beyond the reviewed SHA, adapt this work order to current source rather than restoring the reviewed snapshot mechanically.

---

## 4. Current code facts verified for this work order

### 4.1 `OverlayWindow.xaml` is not currently the size problem

Current `OverlayWindow.xaml` is still a small shell:

```text
AnimationViewport
└─ OpaquePanel
   └─ AnimatedContent
      └─ Grid
         ├─ TabStrip
         └─ BodyScroll
            └─ TabBody
```

Do **not** redesign or decompose the XAML shell in PR-A.

The primary maintainability issue is the large code-behind, not the current XAML file.

### 4.2 `OverlayWindow.xaml.cs` currently mixes four clear responsibilities

The existing file naturally separates into these responsibility groups.

#### Presentation/window lifecycle

Current methods include:

```text
PrepareHidden
ShowForPocAsync
HideForPocAsync
ConfigureWindow
LogSurfaceBounds
SetVisibleVisualState
SetHiddenVisualState
TrySetVisibleVisualState
SetVisualState
AnimateAsync
SetAnimationCenterPoint
AnimationRasterizationScale
AnimationsEnabled
```

These methods own only the visible WinUI presentation around the already-established native `WindowInterop` policy.

#### Shell/tab/Shortcut surface

Current methods/state include:

```text
BuildShell
BuildPage
BuildTabOrderEditorPage
BuildShortcutPage
OnTabHeaderClick
ResetUiForShow
SelectPreviousTab
SelectNextTab
ApplyTabOrderState
RequestTabOrderMove
ApplySelectedHeaderVisual
ApplySelectedTabVisualState
LabelFor
CreatePlaceholderPage

_tabState
_tabButtons
_tabPages
_tabOrderRows
_shortcutSelection
_shortcutTiles
TabOrderMoveRequested
```

#### Logical navigation/selection

Current methods/state include:

```text
NavigateUp
NavigateDown
AdjustSelectedRow
ActivateSelectedRow
MoveRowSelection
RefreshRowSelectionAfterMove
ApplyRowSelectionVisual
BringSelectedRowIntoView
CapabilitiesFor
OnShortcutPage
SelectShortcutSlot
ApplyShortcutSelectionVisual

OverlayRow
_pageRows
_rowSelection
```

The existing `OverlayRowSelection` and `OverlayRowCapabilities` model is already narrow and should remain the selection authority.

#### Generic Quick Settings renderer/binding

Current methods/state include:

```text
ConfigureQuickSettings
ConfigureQuickSettingsSurface
ApplyQuickSettingsPage
RenderQuickSettingsPage
ApplyQuickSettingsLocalFailure
QuickSettingsRowShapeOf
QuickSettingsSectionShapeOf
UpdateQuickSettingsRowValues
ApplyQuickSettingsToggleState
ApplyQuickSettingsSliderState
RebuildQuickSettingsContent
CreateQuickSettingsMessageText
TryCreateQuickSettingsRow
CreateQuickSettingsToggleRow
SubmitQuickSettingsToggleAsync
CreateQuickSettingsSliderRow
ScheduleQuickSettingsSlider
FormatDiscreteLabel

QuickSettingsSurface
QuickSettingsRowShape
_quickSettingsSurfaces
```

This is the current generic Device/Profile frontend renderer and must not be functionally rewritten in PR-A.

### 4.3 `App.xaml.cs` depends on stable `OverlayWindow` seams

Current `App` calls/subscribes to these `OverlayWindow` members:

```text
OutsideClickDismissRequested
TabOrderMoveRequested
HandleForDiagnostics
PrepareHidden()
ConfigureQuickSettings(...)
ApplyQuickSettingsPage(...)
ApplyTabOrderState(...)
SelectPreviousTab()
SelectNextTab()
NavigateUp()
NavigateDown()
AdjustSelectedRow(...)
ActivateSelectedRow()
ShowForPocAsync()
HideForPocAsync()
```

Keep these seams unchanged in PR-A.

Do not make `App` aware of the new partial-file organization.

### 4.4 Current native window policy is already correct

`WindowInterop` owns:

- monitor selection;
- provisional target-monitor placement;
- target-window DPI acquisition;
- final large floating geometry;
- `WS_EX_NOACTIVATE`;
- `WS_EX_TOOLWINDOW`;
- topmost-band promotion/reassertion;
- DWM rounded-corner preference;
- outside-click watcher;
- native diagnostics.

Do **not** move these responsibilities into `OverlayWindow` partial files.

Do **not** refactor `WindowInterop` merely for symmetry.

### 4.5 Current controller authority is outside Overlay UI

The controller path remains:

```text
PID1902 physical input
→ Runtime ControllerState
→ Runtime Overlay capture
→ semantic OverlayNavigationAction
→ .Overlay pipe
→ App.xaml.cs dispatcher
→ OverlayWindow
```

Do not add:

- XInput;
- GameInput;
- `Windows.Gaming.Input`;
- DirectInput;
- Steam Input reads;
- keyboard/gamepad focus synthesis;
- another controller navigation owner.

---

## 5. Target structural shape

Use C# partial-class files to separate responsibilities while preserving **one logical `OverlayWindow` object**.

Recommended target:

```text
src/SteamInputAddonforClaw.Overlay/
│
├─ OverlayWindow.xaml
├─ OverlayWindow.xaml.cs
├─ OverlayWindow.Presentation.cs
├─ OverlayWindow.Shell.cs
├─ OverlayWindow.Navigation.cs
└─ OverlayWindow.QuickSettings.cs
```

This exact four-way split is the recommended starting point because it matches current code boundaries cleanly.

A small adjustment is acceptable if current main makes one boundary clearly better, but do not create more files merely to reduce line counts.

### 5.1 `OverlayWindow.xaml.cs`

Keep this as the composition root for the window.

It should primarily contain:

- constructor;
- `InitializeComponent()`;
- shared resource/brush initialization needed by multiple responsibilities;
- `Closed` cleanup wiring;
- truly cross-cutting state that has no clearer owner;
- very small common helpers only when genuinely shared.

Do not force every field into this file.

Fields may live in the partial file that owns their responsibility.

### 5.2 `OverlayWindow.Presentation.cs`

Move current presentation/window-visual lifecycle here:

```text
PrepareHidden
ShowForPocAsync
HideForPocAsync
ConfigureWindow
LogSurfaceBounds
SetVisibleVisualState
SetHiddenVisualState
TrySetVisibleVisualState
SetVisualState
AnimateAsync
SetAnimationCenterPoint
AnimationRasterizationScale
AnimationsEnabled
```

Move related constants/state such as:

```text
HiddenScale
HiddenTranslateYDip
HiddenOpacity
ShowDuration
HideDuration
_lastConfiguredDpi
```

to this partial unless a current-main dependency requires otherwise.

Do not change the current animation values or sequencing.

### 5.3 `OverlayWindow.Shell.cs`

Move top-level shell/tab/order/page-construction responsibility here:

```text
BuildShell
BuildPage
BuildTabOrderEditorPage
RequestTabOrderMove
OnTabHeaderClick
ResetUiForShow
SelectPreviousTab
SelectNextTab
ApplyTabOrderState
ApplySelectedHeaderVisual
ApplySelectedTabVisualState
LabelFor
CreatePlaceholderPage
```

Also keep shell-owned state here when practical:

```text
_tabState
_tabButtons
_tabPages
_tabOrderRows
TabOrderMoveRequested
```

`BuildShortcutPage` may remain in this partial if that keeps page construction coherent, while navigation-specific Shortcut selection behavior may live in `Navigation`.

Do not create a `TabManager`, `PageManager`, or page registry.

### 5.4 `OverlayWindow.Navigation.cs`

Move the one logical controller-selection/navigation path here:

```text
OverlayRow
_pageRows
_rowSelection
_shortcutSelection
_shortcutTiles

NavigateUp
NavigateDown
AdjustSelectedRow
ActivateSelectedRow
MoveRowSelection
RefreshRowSelectionAfterMove
CapabilitiesFor
ApplyRowSelectionVisual
BringSelectedRowIntoView
OnShortcutPage
SelectShortcutSlot
ApplyShortcutSelectionVisual
```

If `BuildShortcutPage` is kept in `Shell`, sharing `_shortcutTiles` across the partial class is fine.

Do not add a separate navigation service/interface.

Do not migrate to WinUI native gamepad focus, XYFocus, or FocusEngagement in this PR.

### 5.5 `OverlayWindow.QuickSettings.cs`

Move the current generic Device/Profile renderer/binding into this partial:

```text
QuickSettingsSurface
QuickSettingsRowShape
_quickSettingsSurfaces

ConfigureQuickSettings
ConfigureQuickSettingsSurface
ApplyQuickSettingsPage
RenderQuickSettingsPage
ApplyQuickSettingsLocalFailure
QuickSettingsRowShapeOf
QuickSettingsSectionShapeOf
UpdateQuickSettingsRowValues
ApplyQuickSettingsToggleState
ApplyQuickSettingsSliderState
RebuildQuickSettingsContent
CreateQuickSettingsMessageText
TryCreateQuickSettingsRow
CreateQuickSettingsToggleRow
SubmitQuickSettingsToggleAsync
CreateQuickSettingsSliderRow
ScheduleQuickSettingsSlider
FormatDiscreteLabel
```

Keep the existing renderer behavior exactly.

PR-B will later replace the visual Slider representation with the new ValueRow foundation.

PR-A must not pre-implement that future change.

---

## 6. Partial-class rules

All files remain the same logical type:

```csharp
namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    ...
}
```

At least one partial declaration retains the existing `: Window` base declaration as required by the current XAML-generated partial type.

Do not introduce inheritance or composition wrappers around `OverlayWindow`.

Do not introduce:

```text
OverlayWindowController
OverlayShellManager
OverlayNavigationManager
OverlayPresentationManager
OverlayQuickSettingsRenderer service
IOverlayPage
IOverlayRow
OverlayViewModel framework
dependency-injection registration
event bus
mediator
```

The partial split is an organizational boundary, not a new runtime architecture.

---

## 7. Preserve exact behavior

This PR has a strict **behavior-preserving** contract.

### 7.1 Show/Hide

Preserve current ordering:

```text
Show
→ reset UI for Show
→ ConfigureWindow
→ prepare hidden visual state
→ ShowWithoutActivation
→ arm outside-click dismissal
→ animate when enabled
→ settle visible visual state

Hide
→ cancel unsubmitted Quick Settings drafts
→ disarm outside-click dismissal
→ animate when enabled
→ WindowInterop.Hide
→ settle hidden visual state
```

Do not alter durations, scale, translation, opacity, easing, topmost behavior, or native geometry.

### 7.2 Tabs

Preserve:

- five known tab identities;
- current Runtime-authoritative order;
- first tab in configured order selected on every Show;
- pointer/touch tab selection;
- LB/RB bounded previous/next behavior;
- current Setting tab-order editor;
- authoritative reorder result handling;
- current page visibility and scroll reset behavior.

Do not redesign the tab strip in PR-A.

### 7.3 Row navigation

Preserve:

```text
Up / Down
→ move logical row selection

Left / Right
→ dispatch Adjust to selected row

A
→ dispatch Activate to selected row

selected row disappears/becomes unavailable
→ normalize selection safely

row moves out of viewport
→ BringIntoView
```

Do not change selected-row visual treatment.

Do not add the future `TrySelect(index)` touch-selection enhancement yet unless current behavior already requires it to preserve an existing path.

That enhancement belongs to the later Foundation integration PR.

### 7.4 Shortcut

Preserve the current fixed 2x2 Shortcut shell and selection behavior exactly.

Do not implement Shortcut actions or preference changes.

### 7.5 Quick Settings

Preserve:

- Device/Profile generic rendering;
- row-shape fast path;
- AppId/section-text identity guard;
- visible-row filtering;
- malformed-row fail-closed behavior;
- authoritative result/readback;
- local failure banner;
- current toggle submission;
- current numeric/discrete Slider behavior;
- delayed/grouped slider binding semantics;
- selected RowId preservation across structural rebuild;
- cancellation of unsubmitted drafts during Hide.

Do not change the shared Quick Settings contracts.

---

## 8. Preserve current ownership and lifecycle contracts

Zero behavior change is allowed to:

- Center M Enabled/Disabled authority;
- PID1901/PID1902;
- DirectInput ownership;
- HidHide;
- VIIPER server/bus;
- X360/SteamDeck presentation selection;
- Steam/BPM observation;
- Overlay capture admission;
- neutral publication;
- release-to-resume gate;
- physical controller loss/recovery;
- sleep/hibernate/resume;
- WING/OEM1;
- M1/M2;
- rumble;
- gyro;
- TDP;
- fan control;
- Profile authority;
- settings persistence.

This PR should not need to touch Runtime controller/lifecycle source at all.

---

## 9. Preserve transport

No `.Overlay` protocol change is expected.

Do not change:

```text
OverlayWireMessageKind
OverlayCommand
OverlayNavigationAction
OverlayState
protocol version
NamedPipeOverlayClient
Runtime command/navigation dispatch
```

`App.xaml.cs` should remain behaviorally unchanged.

A small source-only adjustment is acceptable only if required by compiler visibility after moving a member, but do not redesign App/Window responsibility.

Preferred outcome: `App.xaml.cs` has no functional diff.

---

## 10. XAML scope

Do not redesign `OverlayWindow.xaml` in PR-A.

Current shell structure from PR #559 remains the baseline.

Do not add:

- new sections;
- new row styles;
- a footer;
- controller hints;
- ValueRow;
- ActionRow;
- StatusRow;
- ComboBox;
- new Slider styling;
- two-column content;
- content-width policy;
- right-side status pane.

If a zero-behavior XAML change is required only to support source-file generation/refactoring, keep it minimal and explain it in the PR description.

Prefer no XAML diff.

---

## 11. Existing row classes remain intact for PR-A

Do not refactor these merely because the new Foundation will later replace/reshape them:

```text
OverlayToggleRow
OverlaySliderRow
OverlaySliderModel
OverlayToggleModel
OverlayRowSelection
OverlayTabState
OverlayShortcutSelection
AddonQuickSettingsTabOrderRow
OverlayQuickSettingsPageBinding
```

The future PR-B will introduce the controller-first row primitive layer.

PR-A should make that future change easier, not implement it early.

---

## 12. Cleanup allowed in PR-A

Because this is a structural refactor, narrowly scoped cleanup is allowed when it directly follows from the file split.

Allowed examples:

- remove comments whose only purpose was explaining an old file-local ordering that no longer exists;
- update comments to reference the new responsibility/file correctly;
- move private nested record/type declarations next to the methods that own them;
- reorder private fields within a responsibility group for readability;
- remove genuinely duplicate `using` directives after the split;
- use file-local/private helper placement that makes ownership clearer without changing behavior.

Do not turn this into a broad naming/style cleanup.

Do not rename public/internal seams used by tests or `App`.

Do not change product terminology.

---

## 13. Do not overengineer the refactor

The purpose is to reduce mixed responsibilities, not to maximize file count or abstraction count.

Specifically do **not** add:

```text
new interface hierarchy
new base classes
new manager/service classes
new dependency injection
new navigation graph
new state machine
new UI event bus
new page registry
new row factory framework
new view-model framework
generic renderer abstraction
generic command dispatcher
new locks/epochs/barriers
```

A partial-class split is intentionally chosen because:

- `OverlayWindow` is still the one real WinUI owner;
- XAML named elements remain directly available;
- existing state remains in one object;
- no new ownership boundary is invented;
- later Foundation work can still extract a real component only when more than one real consumer proves the abstraction useful.

The target is **one clear UI owner with separated source responsibilities**, not architectural layering for its own sake.

---

## 14. Expected implementation files

Expected new/changed files:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shell.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Navigation.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.QuickSettings.cs
```

Expected unchanged unless current main requires a narrow compiler/test adjustment:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/WindowInterop.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlaySliderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs
src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs
src/SteamInputAddonforClaw.FrontendTransport/*
src/SteamInputAddonforClaw/Lifecycle/*
```

Tests may require narrow source-contract updates if they read `OverlayWindow.xaml.cs` by path.

Do not weaken those tests. Update helpers so they inspect the new partial source set deliberately.

---

## 15. Test impact — important source-contract issue

Current Overlay tests include source-text assertions that read specific files, especially:

```text
tests/SteamInputAddonforClaw.Tests/OverlayDeviceRendererWiringTests.cs
```

Some current assertions use helpers equivalent to:

```text
ReadOverlayWindowSource()
→ reads only OverlayWindow.xaml.cs
```

After the partial split, those assertions will otherwise fail even though the required code still exists.

Do **not** work around this by moving methods back into the main code-behind solely to satisfy old test helpers.

Instead, make the test helper reflect the new source organization explicitly.

Recommended test helper shape:

```csharp
private static string ReadOverlayWindowSources() =>
    string.Join(
        Environment.NewLine,
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs"));
```

or use narrower responsibility-specific readers where a test is specifically validating Presentation/Shell/QuickSettings.

Prefer **narrow readers** when practical, because they improve the architecture guard:

```text
presentation contract assertion
→ read Presentation partial

Quick Settings renderer assertion
→ read QuickSettings partial

navigation assertion
→ read Navigation partial
```

Do not use glob/reflection machinery just to locate source files dynamically.

Keep the source-contract test structure simple and explicit.

---

## 16. New structural tests

Add narrow tests that protect the purpose of this refactor without asserting arbitrary line counts.

### 16.1 Main code-behind is no longer the feature dumping ground

Add a source architecture assertion proving that the main `OverlayWindow.xaml.cs` does not contain the major responsibility methods moved out.

For example, it should no longer contain method declarations such as:

```text
RenderQuickSettingsPage
RebuildQuickSettingsContent
BuildTabOrderEditorPage
NavigateUp
AnimateAsync
```

Do not assert an exact maximum number of lines.

The goal is responsibility ownership, not LOC gaming.

### 16.2 Partial responsibility files exist

Assert the expected responsibility files are present and contain their key ownership seams.

Examples:

```text
Presentation
→ ShowForPocAsync
→ HideForPocAsync
→ AnimateAsync

Shell
→ BuildShell
→ ApplyTabOrderState
→ ApplySelectedTabVisualState

Navigation
→ NavigateUp
→ AdjustSelectedRow
→ BringSelectedRowIntoView

QuickSettings
→ ConfigureQuickSettings
→ RenderQuickSettingsPage
→ RebuildQuickSettingsContent
```

### 16.3 One `OverlayWindow` owner

Do not create a test that bans all future helper classes.

Instead, assert the new responsibility files remain `partial class OverlayWindow` and that no specifically prohibited parallel authority was introduced in this PR.

A simple source assertion may check the new files contain:

```text
partial class OverlayWindow
```

and do not contain known unnecessary types such as:

```text
class OverlayNavigationManager
class OverlayPresentationManager
class OverlayShellManager
class OverlayQuickSettingsManager
```

Keep this focused; do not build a generalized architecture linter.

---

## 17. Existing behavior tests that must stay green

Do not weaken or delete current tests covering:

- Overlay tab state/order normalization;
- LB/RB semantics;
- logical row selection;
- Shortcut selection;
- Toggle row;
- Slider row/model;
- generic Quick Settings binding;
- Device/Profile generic renderer source contracts;
- Quick Settings transport;
- delayed/grouped commit semantics;
- Overlay transport;
- controller capture/release;
- outside-click behavior;
- geometry;
- topmost/no-activate source contracts;
- Full1902 lifecycle.

If a test fails only because a source symbol moved to a partial file, update the source-location helper.

If a behavioral test fails, treat that as a refactor regression and fix the implementation rather than changing expected behavior.

---

## 18. Build / validation

Run:

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx --no-restore -v:minimal
dotnet build SteamInputAddonforClaw.slnx --configuration Release --no-restore -v:minimal
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj
git diff --check
```

No new warnings should be introduced.

---

## 19. Manual regression validation

This is a structural PR, but perform a short hardware/manual sanity pass because the affected type is the live Overlay window.

At minimum verify on the supported MSI Claw:

### Show / Hide

```text
open Overlay
→ current large floating window appears
→ same geometry as PR #559
→ same gray surface / rounded window
→ same show animation

close Overlay
→ same hide animation
→ capture retires normally
```

### Controller navigation

Verify:

```text
LB/RB
→ tabs still move exactly as before

DPad / sticks
→ row navigation still works

Left/Right
→ current Slider rows still adjust exactly as before

A
→ current Toggle rows still activate exactly as before

B
→ Overlay still closes through the existing Runtime dismissal path
```

### Pointer/touch

Verify:

- tab touch still selects a tab;
- current Toggle/Slider pointer interaction still works;
- outside click/touch still follows the existing dismissal path.

### Quick Settings

Verify existing Device/Profile content still renders and mutates as before.

This PR does not need a new visual-UX acceptance pass because it intentionally does not change the UI design.

---

## 20. Explicit non-goals

Do not implement any part of Foundation PR-B/C in this PR.

Specifically, do not add or change:

- `OverlayValueRow`;
- `OverlayActionRow`;
- `OverlayStatusRow`;
- new `OverlayShortcutTile` visual component;
- Slider → ValueRow conversion;
- ComboBox/dropdown behavior;
- new selected-row affordance;
- touch → logical-row-selection synchronization;
- section visual redesign;
- content-width/max-width policy;
- Overlay window width;
- two-column layout;
- right-side status pane;
- bottom controller hints;
- nested pages;
- modal/back navigation;
- new B-button policy;
- outside-click mode preference;
- WinUI XYFocus;
- FocusEngagement;
- NavigationView migration;
- native WinUI gamepad input;
- QAM removal;
- desktop UI feature migration.

This PR only prepares the codebase so those later decisions can be implemented cleanly.

---

## 21. Review criteria

Treat the following as blocking regressions.

### Blocking

- any user-visible behavior changes unintentionally;
- Show/Hide sequencing changes;
- topmost/no-activate/geometry/animation regression;
- outside-click dismissal regression;
- tab order/start-tab behavior changes;
- LB/RB/DPad/stick/A/B semantic navigation regression;
- logical row selection/scroll regression;
- Device/Profile Quick Settings mutation/readback regression;
- delayed/grouped slider mutation regression;
- Shortcut selection regression;
- `.Overlay` protocol change;
- Runtime/controller authority source touched without a concrete necessity;
- new controller reader in Overlay;
- new manager/service/interface/state authority introduced merely for code organization;
- tests weakened instead of adapted to the partial-file organization.

### Non-blocking

- exact private-method ordering within the recommended responsibility file;
- minor comment wording;
- one responsibility file ending somewhat larger/smaller than another;
- keeping a truly cross-cutting private field in `OverlayWindow.xaml.cs` rather than forcing artificial ownership.

### Theoretical / do not block

Do not add synchronization, epochs, locks, wrappers, or state machines for hypothetical timing intersections introduced only by moving methods between partial files.

The refactor must preserve the current real lifecycle behavior; it does not create a new concurrency model.

---

## 22. Acceptance checklist

### Structure

- [ ] `OverlayWindow` remains one partial class / one UI owner.
- [ ] Main `OverlayWindow.xaml.cs` is reduced to composition-root/cross-cutting responsibility.
- [ ] Presentation/window visual lifecycle lives in a dedicated partial.
- [ ] Shell/tab/order responsibility lives in a dedicated partial.
- [ ] Logical row/Shortcut navigation lives in a dedicated partial.
- [ ] Generic Quick Settings renderer/binding lives in a dedicated partial.
- [ ] No new manager/service/interface framework is introduced.

### Behavior

- [ ] Visible Overlay UI is unchanged.
- [ ] PR #559 large-window geometry/presentation is unchanged.
- [ ] Show/Hide behavior is unchanged.
- [ ] Tab behavior is unchanged.
- [ ] Controller semantic navigation is unchanged.
- [ ] Shortcut selection is unchanged.
- [ ] Device/Profile Quick Settings rendering/mutation is unchanged.
- [ ] Outside-click dismissal is unchanged.
- [ ] App/Runtime transport contract is unchanged.

### Tests

- [ ] Source-contract readers are updated for partial files without weakening assertions.
- [ ] Existing Overlay tests pass.
- [ ] Existing Full1902 lifecycle tests pass.
- [ ] New narrow structural guards cover the new responsibility layout.
- [ ] Debug build passes.
- [ ] Release build passes.
- [ ] Full test suite passes.
- [ ] `git diff --check` passes.

---

## 23. Follow-up boundary

After this PR is merged and hardware sanity is clean, proceed to the separate controller-first UI primitive PR.

That later PR will be allowed to change the visual row language:

```text
OverlaySliderRow
→ OverlayValueRow

plus
OverlayActionRow
OverlayStatusRow
ToggleRow visual cleanup
```

PR-A must leave a clean seam for that work, but must not implement it.
