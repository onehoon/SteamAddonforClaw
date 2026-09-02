# Work Order — OQ5-UI-01: Five-Tab Overlay Shell

## Status

First implementation PR from:

- `docs/overalyui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`

Track: Addon Quick Settings Overlay UI

Label/name: `OQ5-UI-01`

This is **not** part of the numbered Full PID1902 PR sequence.

---

## 1. Goal

Replace the current visible Overlay POC diagnostic stack with the stable top-level Quick Settings UI shell that all later OQ5 UI/feature PRs will populate.

After this PR, every successful Overlay Show should present:

```text
┌────────────────────────────────────┐
│ Quick Settings                     │
│                                    │
│ Device Profile Controller Shortcut Setting
│ ─────────────────────────────────  │
│                                    │
│      active tab placeholder        │
│                                    │
│                                    │
│ B  Close                           │
└────────────────────────────────────┘
```

The exact visual spacing may use normal WinUI layout behavior, but the shell hierarchy is frozen:

```text
OpaquePanel
└─ AnimatedContent
   ├─ Header
   ├─ horizontal Tab strip
   ├─ scrollable Body
   └─ Footer / controller hint region
```

This PR is intentionally only the shell.

Do not add real Device/Profile/Controller/Shortcut/Setting feature controls yet.

---

## 2. Required reading before implementation

Read current `main`, not an older Overlay POC branch.

Required documents:

- `docs/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ3_A_MAIN_UI_OVERLAY_VISIBLE_SURFACE_COEXISTENCE_WORK_ORDER.md`
- `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`
- `docs/overalyui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`

Required current source:

- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs`
- `src/SteamInputAddonforClaw.Overlay/WindowInterop.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`

Current code facts that must guide the change:

1. `OverlayWindow.xaml` currently has:

```text
AnimationViewport
→ OpaquePanel (#FFF3F3F3, Padding=28)
→ AnimatedContent
→ visible POC diagnostic TextBlocks
```

2. `OverlayWindowGeometry.PocPanelWidthDip` is currently 400 DIP.

3. `OverlayWindow.ShowForPocAsync()` currently owns:

```text
ConfigureWindow
→ prepare hidden animation state
→ ShowWithoutActivation
→ arm outside-click dismissal
→ show animation
→ final visible visual state
```

4. `OverlayWindow.HideForPocAsync()` owns the corresponding hide/disarm behavior.

5. `ElementCompositionPreview.GetElementVisual(AnimatedContent)` is the existing animation seam.

6. `App.HandleNavigationAsync()` currently logs semantic navigation, writes the navigation diagnostic TextBlock, and routes `Back` to the existing Runtime-owned dismiss path.

7. OQ4 already owns capture/neutral/release behavior. This PR must not create another input/capture lifecycle.

---

## 3. Frozen shell contract

### 3.1 Top-level tabs

Create exactly five top-level tab identities:

```text
Device
Profile
Controller
Shortcut
Setting
```

Default order is exactly the order above.

All five pages must exist in this PR even though their bodies are placeholders.

Do not hide empty tabs.

Do not add any sixth tab.

### 3.2 Startup tab behavior

Every successful Overlay Show starts on the **first tab in the current tab order**.

For this PR the only available order is the default order, therefore:

```text
every Show
→ Device selected before the window is visually revealed
```

This must also be true when the same warm Overlay process was previously showing another tab.

Example:

```text
Show
→ Device

pointer selects Shortcut
→ Shortcut visible

Hide

Show again
→ Device
```

Do not remember the previous tab across Hide/Show.

Do not persist anything in this PR.

The implementation should express the rule as:

```text
SelectedTab = CurrentTabOrder[0]
```

rather than hard-coding a separate `DefaultTab = Device` fact. Later tab-order persistence will replace `CurrentTabOrder` without introducing another default-tab authority.

### 3.3 Pointer/touch behavior

Pointer/touch selection of the tab headers should switch the visible placeholder page immediately.

This does **not** require window activation and must not alter the existing no-activate top-level window policy.

### 3.4 Controller behavior in this PR

Keep only currently implemented controller behavior.

- `B / Back` continues to request Overlay dismissal through the existing OQ4 path.
- existing Up/Down/Left/Right/Accept semantic messages may continue to be logged but do not need to control the shell yet.
- do **not** implement LB/RB tab switching here.
- do **not** implement stick navigation here.

The next PR owns LB/RB transport/router changes.

---

## 4. Visual/layout requirements

### 4.1 Preserve current window surface

Do not change:

- left alignment;
- WorkArea-height behavior;
- 400 DIP target width;
- `#FFF3F3F3` opaque surface;
- taskbar avoidance;
- borderless/topmost/no-activate behavior;
- show/hide durations;
- current translation/opacity animation behavior;
- outside-click dismissal lifecycle.

### 4.2 Preserve the animation names/seam

The following XAML names remain available because existing code and diagnostics use them:

```text
AnimationViewport
OpaquePanel
AnimatedContent
```

`AnimatedContent` may change from the current simple `StackPanel` to a `Grid`, but it must remain the single content visual animated by the current composition code.

Do not create a second animation root merely for the tabs/body.

### 4.3 Keep the existing content inset

The current `OpaquePanel` already has `Padding="28"`.

Keep that outer inset for this PR.

Do not return to edge-to-edge text/controls.

Organize spacing with ordinary WinUI layout margins/row spacing inside the 28 DIP panel padding.

Do not add a theme/token framework.

### 4.4 Header

Use normal WinUI typography resources/defaults rather than introducing a custom font-scale system.

Header should identify the surface as Quick Settings.

Do not keep `Overlay POC-A` as user-facing product copy.

A small product subtitle is optional only if it fits naturally; do not spend this PR building header metadata/state.

### 4.5 Tab strip

Requirements:

- horizontal;
- all five labels visible within the current panel;
- selected tab is visually distinct;
- pointer/touch hit target is usable;
- no close buttons;
- no add-tab button;
- no scrollable browser-style tab chrome;
- no custom font size requirement;
- do not shrink the entire UI merely to fit the strip.

A simple five-column WinUI layout is preferred for this first PR.

Do not introduce a generalized tab framework.

The implementation must nevertheless keep tab identity separate from display text so later persisted order can reorder known IDs without treating localized labels as authority.

### 4.6 Body

Create one scrollable content region.

Each tab must have its own page/container identity.

For this PR the body may contain only a neutral placeholder such as the page name or an empty-state message.

Do not show fake TDP/FPS/CPU values.

Do not add sample controls that look functional but are not Runtime-backed.

The body should be ready for later real rows without redesigning the whole window.

### 4.7 Footer

Reserve a footer/hint region.

In this PR it may show only behavior that actually exists, for example:

```text
B  Close
```

Do **not** display `LB/RB Tabs` before OQ5-UI-02 implements it.

Do not show LT/RT/X/Y hints because those inputs are reserved/empty.

---

## 5. Narrow tab-state model

Add only the minimum UI state necessary for the five known tabs and the future reorder seam.

Recommended shape:

```csharp
internal enum OverlayTabId
{
    Device,
    Profile,
    Controller,
    Shortcut,
    Setting,
}
```

and one small local state/catalog representation conceptually equivalent to:

```csharp
internal static readonly OverlayTabId[] DefaultOrder =
[
    OverlayTabId.Device,
    OverlayTabId.Profile,
    OverlayTabId.Controller,
    OverlayTabId.Shortcut,
    OverlayTabId.Setting,
];

private IReadOnlyList<OverlayTabId> _tabOrder = DefaultOrder;
private OverlayTabId _selectedTab;
```

A tiny testable `OverlayTabState` class is acceptable if it keeps selection/order logic out of XAML event handlers, but do not create:

- `OverlayNavigationManager`;
- `OverlayPageManager`;
- `TabAuthority`;
- generic view-model infrastructure;
- dependency-injection setup for five tabs.

Required operations are only:

```text
get current order
select known tab
reset selection to order[0] before Show
```

Unknown tab identity should never be accepted as valid local state.

---

## 6. Show ordering requirement

The selected startup tab must be committed **before** the Overlay becomes visible.

Do not do this after `ShowWithoutActivation`, because that can flash the previously selected page for one frame.

Preferred ordering inside the existing `ShowForPocAsync()` seam:

```text
ResetUiForShow()
→ ConfigureWindow()
→ set hidden animation visual state
→ ShowWithoutActivation()
→ arm outside-click dismissal
→ animate visible
```

`ResetUiForShow()` should:

1. select `_tabOrder[0]`;
2. update selected-tab visual state;
3. update which page/body is visible;
4. not perform persistence or IPC.

The warm process remains warm; do not recreate `OverlayWindow` on each Show simply to obtain default state.

---

## 7. Remove user-visible POC diagnostics without removing useful logging

The current shell exposes:

- geometry text;
- `Overlay POC-A` text;
- latest navigation diagnostic text.

These should no longer appear in normal user-facing Overlay content after this PR.

### Geometry

`ConfigureWindow()` still needs to call `WindowInterop.Configure(...)` and retain `_lastConfiguredDpi` because actual placement/diagnostic logging uses it.

Remove only the dependency that writes geometry values into a visible `GeometryText` TextBlock.

Keep `LogSurfaceBounds(...)` and existing low-rate Overlay logs.

### Navigation

`App.HandleNavigationAsync()` should no longer call a visible `ShowNavigationDiagnostic()` method.

Keep its normal debug logging.

Keep:

```text
Back
→ SendBackDismissAsync()
→ DismissRequested
→ Runtime unified capture retirement
```

Do not move B-close authority into `OverlayWindow` merely because the visible diagnostic is removed.

---

## 8. Expected files

Primary expected changes:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
```

Optional one small new file if it materially improves clarity/testability:

```text
src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs
```

Tests may be added under:

```text
tests/SteamInputAddonforClaw.Tests/
```

Do not modify Runtime controller/presentation code for this PR.

Do not modify `.Overlay` wire protocol for this PR.

Do not modify QamHost.

Do not modify settings persistence.

Do not modify `WindowInterop` or `OverlayWindowGeometry` unless a concrete shell regression proves it necessary; layout work should fit the already-proven window geometry.

---

## 9. Tests

Prefer behavior tests around any extracted non-XAML tab state rather than large brittle source-text tests.

Minimum logical coverage when a small tab-state class is used:

### Default catalog

Assert:

```text
count == 5
all IDs unique
order == Device, Profile, Controller, Shortcut, Setting
```

### Selection

Assert each known tab can become selected.

### Show reset

```text
select Shortcut
reset for Show
→ Device selected
```

Later tab-order persistence is not part of this PR, but if the state type accepts an order, also prove reset means `order[0]`, not a separate hard-coded Device rule.

Example:

```text
order = Controller, Device, Profile, Shortcut, Setting
select Setting
reset
→ Controller
```

That test protects the future frozen product rule without implementing persistence.

### Existing regression coverage

Existing Overlay transport/show/hide/dismiss tests must remain green.

Do not weaken OQ4 tests to make the shell pass.

---

## 10. Manual validation

Run the Overlay through the existing Runtime path, not by creating a separate debug-only window lifecycle.

Validate:

1. Overlay opens at the same left-side position and width as before.
2. Taskbar is still not covered.
3. Game/desktop foreground is not stolen.
4. Show animation still slides/fades the full `AnimatedContent` shell.
5. Five tab labels are visible at 1920×1200 / 150% scaling.
6. Device is selected on first Show.
7. pointer/touch selects each of the five pages.
8. Hide from Shortcut/Setting, then Show again → Device is selected before the panel becomes visible.
9. outside click still dismisses.
10. B still dismisses through Runtime/OQ4.
11. no geometry/navigation POC diagnostic text is visible.
12. Overlay remains a warm hidden process after Hide.

If five default WinUI tab headers do not fit comfortably at 400 DIP, first reduce only tab-strip internal padding/spacing as needed. Do not change the 400 DIP window width or globally scale down WinUI controls in this PR.

---

## 11. Build/verification

Required before PR completion:

```text
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test
git diff --check
```

If the full suite exposes a known pre-existing unrelated flaky test, prove it reproduces on the unchanged baseline before claiming it is unrelated.

No hardware-only behavior should be marked validated unless it was actually tested on a supported MSI Claw.

---

## 12. Acceptance criteria

The PR is complete when all of the following are true:

- [ ] visible POC diagnostic stack is replaced by the structured shell;
- [ ] five tabs exist: Device/Profile/Controller/Shortcut/Setting;
- [ ] all five pages exist, even if placeholder-only;
- [ ] default tab order is exactly the frozen order;
- [ ] current tab identity is separate from visible label text;
- [ ] pointer/touch tab selection works;
- [ ] every Show resets selection to the first entry in current order **before visual reveal**;
- [ ] no last-tab persistence exists;
- [ ] no separate default-tab setting exists;
- [ ] 400 DIP geometry is unchanged;
- [ ] `#FFF3F3F3` surface is unchanged;
- [ ] outer 28 DIP content inset remains;
- [ ] standard WinUI control/typography sizing is used; no custom compact templates are introduced;
- [ ] `AnimationViewport`, `OpaquePanel`, `AnimatedContent` remain valid seams;
- [ ] show/hide animation remains functional;
- [ ] outside-click dismissal remains functional;
- [ ] B close remains Runtime-owned through the existing dismiss path;
- [ ] visible geometry/navigation POC diagnostics are removed while diagnostic logging remains;
- [ ] no Runtime controller/presentation/DirectInput/HidHide/VIIPER behavior changes;
- [ ] no `.Overlay` protocol change;
- [ ] no tab persistence/reorder UI yet;
- [ ] Release build/tests/diff check are clean.

---

## 13. Explicit non-goals

Do not include any of the following in OQ5-UI-01:

- LB/RB tab switching;
- LT/RT behavior;
- X/Y behavior;
- left-stick navigation;
- right-stick navigation;
- logical row selection;
- auto-scroll selection;
- toggle implementation;
- slider implementation;
- QAM-style delayed mutation helper;
- tab-order persistence;
- Setting-page reorder controls;
- Shortcut action assignment/execution;
- TDP/CPU Boost/Power Mode/FPS feature binding;
- Profile feature binding;
- Controller feature binding;
- new theme system;
- custom compact WinUI control templates;
- window geometry redesign;
- OQ3-B Steam QAM visibility exclusion;
- OQ6 physical WING/OEM1 button policy;
- generalized navigation/view-model/framework work.

Keep this PR as the stable visual shell on top of the already-proven OQ4 lifecycle.