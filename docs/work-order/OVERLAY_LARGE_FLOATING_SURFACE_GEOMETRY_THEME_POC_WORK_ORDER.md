
# Work Order — Large Floating Overlay Surface Geometry / Rounded Corners / Neutral WinUI Tone POC

> **Status:** Implementation work order  
> **Prepared:** 2026-09-21  
> **Target repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main baseline:** `d4f16b96e736fb0f572827213e7221f4574427e1`  
> **Architecture authority:** standalone Full PID1902  
> **Scope:** presentation-only expansion of the existing WinUI Overlay window. No controller authority, capture, transport, QAM, feature, or lifecycle redesign.

---

## 1. Goal

Convert the current narrow 400-DIP Quick Settings panel into a **large floating WinUI surface** for hardware evaluation.

This PR is deliberately limited to the outer window/surface presentation:

1. expand the Overlay to occupy most of the 1920×1200 display while leaving a large balanced floating margin;
2. base that margin on approximately one Windows taskbar thickness on all four sides, plus a small additional visual gap;
3. round the real top-level window corners;
4. replace the current near-white `#FFF3F3F3` surface with a neutral light-gray WinUI-style surface;
5. preserve the current Overlay content, navigation, controller capture, no-activate behavior, topmost behavior, show/hide lifecycle, and Runtime authority;
6. hardware-validate that the enlarged Overlay still appears above Steam Big Picture and a representative supported game surface.

This is **not yet** the PR that promotes Overlay to the final product Main UI.

Do not remove QAM in this PR.

Do not redesign the five current Overlay tabs in this PR.

The purpose is to answer:

> Does the existing Addon-owned WinUI Overlay still behave correctly when it becomes a large floating handheld surface rather than a narrow Quick Settings panel?

---

## 2. Mandatory references before editing

Read these current documents together before implementation:

### Full PID1902 authority

1. `docs/Full 1902 Implementation/README.md`
2. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
3. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
4. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`

### Overlay architecture / current presentation behavior

5. `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
6. `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
7. `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
8. `docs/overlayui/OQ5_UI_POLISH_A_FLOATING_SURFACE_COMPACT_TABS_WORK_ORDER.md`
9. `docs/overlayui/OQ5_UI_POLISH_B_REMOVE_TITLE_AND_BALANCE_INTERNAL_CONTENT_WORK_ORDER.md`
10. `docs/work-order/OQ_ZORDER_A_OVERLAY_TOPMOST_PERSISTENCE_WORK_ORDER.md`
11. `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`

Inspect current main source before editing:

- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs`
- `src/SteamInputAddonforClaw.Overlay/WindowInterop.cs`
- `tests/SteamInputAddonforClaw.Tests/OverlayWindowGeometryTests.cs`
- relevant existing Overlay source-contract tests

If current main has moved from the reviewed SHA, adapt this work order to current code rather than restoring an old snapshot mechanically.

---

## 3. Current implementation facts

### 3.1 Current geometry authority

`OverlayWindowGeometry` currently owns:

```text
PocPanelWidthDip = 400
PanelEdgeInsetDip = 4
```

At 150% DPI the current panel is therefore approximately:

```text
width = 600 physical px
outer floating inset = ~6 physical px
```

`WindowInterop.Configure()` calls `OverlayWindowGeometry.Calculate(...)` for the final native rectangle.

Keep **one geometry authority**.

Do not move final sizing rules into XAML, `OverlayWindow`, or another manager.

### 3.2 Current topmost/no-activate path is already correct and must remain

Current `WindowInterop` already has the required handheld Overlay policy:

- `WS_EX_NOACTIVATE`;
- `WS_EX_TOOLWINDOW`;
- `OverlappedPresenter.IsAlwaysOnTop = true`;
- final `SetWindowPos(... HWND_TOPMOST ... SWP_NOACTIVATE ...)`;
- Show-time topmost-band reassertion;
- effective `WS_EX_TOPMOST` diagnostic;
- no foreground activation workaround.

This PR must **not** invent another topmost mechanism.

The enlarged window must reuse the same policy.

### 3.3 Current root tone

`OverlayWindow.xaml` currently uses:

```xml
<Grid x:Name="OpaquePanel" Background="#FFF3F3F3">
```

This is too close to white for the new large surface.

### 3.4 Current controller path does not depend on WinUI gamepad discovery

The existing Overlay controller path is already:

```text
PID1902 physical input
→ Runtime ControllerState
→ Runtime Overlay capture
→ current virtual presentation neutral
→ semantic Overlay navigation
→ .Overlay transport
→ OverlayWindow
```

Do not add XInput, GameInput, `Windows.Gaming.Input`, DirectInput, or Steam Input reads to the Overlay process.

---

## 4. Reference hardware / visual baseline

Primary reference:

```text
Physical display: 1920 × 1200
Windows scale:    150%
DPI:              approximately 144
Effective space:  approximately 1280 × 800 DIP
```

This is a visual baseline, not a hard-coded screen-size contract.

The implementation remains DPI-aware and monitor-relative.

---

## 5. New large floating geometry

### 5.1 Product intent

The large Overlay should visually sit inside the display with roughly **one taskbar thickness of empty space on every side**, plus a small additional floating gap.

Reference visual shape:

```text
┌──────────────────────────── 1920 × 1200 monitor ────────────────────────────┐
│                                                                             │
│        approximately taskbar thickness + small extra gap                    │
│        ┌────────────────────────────────────────────────────────────┐        │
│        │                                                            │        │
│        │                    LARGE ADDON OVERLAY                      │        │
│        │                                                            │        │
│        │                                                            │        │
│        └────────────────────────────────────────────────────────────┘        │
│                  small gap above the real Windows taskbar                   │
│──────────────────────────────── Windows taskbar ────────────────────────────│
└─────────────────────────────────────────────────────────────────────────────┘
```

The Overlay must not touch the taskbar.

The Overlay must not touch the top/left/right monitor edges.

### 5.2 Do not hard-code taskbar pixels

Do not hard-code:

- 72 px taskbar;
- 90 px outer margin;
- 1920;
- 1200;
- 150%.

`WindowInterop.Configure()` already has both:

- `MONITORINFO.rcMonitor`;
- `MONITORINFO.rcWork`.

Use those facts.

Calculate the reserved system-edge widths conceptually as:

```text
reservedLeft   = rcWork.Left   - rcMonitor.Left
reservedTop    = rcWork.Top    - rcMonitor.Top
reservedRight  = rcMonitor.Right  - rcWork.Right
reservedBottom = rcMonitor.Bottom - rcWork.Bottom

reservedEdgePx = max(
    reservedLeft,
    reservedTop,
    reservedRight,
    reservedBottom,
    0)
```

For the normal Claw configuration with the taskbar at the bottom, `reservedBottom` will normally provide the real taskbar reservation.

### 5.3 Keep a sensible handheld baseline even if the taskbar reservation is temporarily zero

Use a taskbar-scale reference of:

```text
48 DIP
```

This corresponds to approximately 72 physical px at 150%.

Conceptually:

```text
referenceTaskbarPx = round(48 DIP × dpi / 96)

baseFloatingMarginPx =
    max(reservedEdgePx, referenceTaskbarPx)
```

This is not a new taskbar manager. It is one geometry calculation.

It prevents auto-hide or an unusual `rcWork` snapshot from collapsing the new large surface back to an almost edge-to-edge window.

### 5.4 Additional visual gap

Add one small DPI-scaled extra gap:

```text
FloatingGapDip = 12
```

At the 150% reference scale:

```text
12 DIP ≈ 18 physical px
```

This directly matches the requested additional approximately 10–20 physical px separation.

Conceptually:

```text
extraGapPx = round(12 × dpi / 96)

outerMarginPx =
    baseFloatingMarginPx + extraGapPx
```

Do not add a user-facing setting.

Do not add independent top/left/right/bottom tuning.

One margin value owns all four sides.

### 5.5 Final rectangle is monitor-relative

The final large rectangle should be conceptually:

```text
X      = rcMonitor.Left + outerMarginPx
Y      = rcMonitor.Top  + outerMarginPx
Width  = monitorWidth  - 2 × outerMarginPx
Height = monitorHeight - 2 × outerMarginPx
```

Clamp safely so width/height never become negative on an unusually small monitor/work area.

Because `outerMarginPx` is at least as large as the largest current reserved system edge, the normal visible taskbar remains outside the Overlay rectangle.

### 5.6 Reference example only

If real hardware reports:

```text
monitor          = 1920 × 1200
DPI              = 144
bottom reservation = 72 px
48 DIP reference = 72 px
12 DIP extra     = 18 px
```

then:

```text
outer margin = 90 px

X      = 90
Y      = 90
Width  = 1740
Height = 1020

Overlay bottom = 1110
taskbar top     = 1128
gap above taskbar = 18 px
```

These are expected example values, not constants.

### 5.7 Replace the obsolete 400-DIP naming

The new geometry is no longer a panel-width POC.

Remove/rename obsolete geometry facts such as:

```text
PocPanelWidthDip
PanelWidthDip
PanelWidthPhysical
```

where they describe the old fixed-width window contract.

Do not leave misleading diagnostics saying the new large Overlay has a 400-DIP panel width.

Useful new diagnostics may include:

```text
MonitorWidth
MonitorHeight
WorkWidth
WorkHeight
ReservedEdgePx
ReferenceTaskbarPx
ExtraGapPx
OuterMarginPx
OverlayWidthPx
OverlayHeightPx
```

Keep logging bounded to the existing Configure/Show lifecycle. No polling.

---

## 6. Provisional DPI placement

Current `WindowInterop.Configure()` performs provisional placement before calling `GetDpiForWindow(hwnd)`.

The current provisional path still references the old 400-DIP width.

Remove that dependency.

Keep the existing intent:

```text
move hidden Overlay HWND onto target monitor
→ obtain target-window DPI
→ calculate final DPI-aware rectangle
→ final SetWindowPos
```

A simple monitor/work-area-sized provisional rectangle is acceptable because the window is still hidden.

Do not replace the current Per-Monitor V2 path with a new DPI abstraction merely for this PR.

---

## 7. Real rounded top-level corners

### 7.1 Use Windows 11 DWM corner preference

The product is Windows 11 only.

Use the native DWM preference:

```text
DWMWA_WINDOW_CORNER_PREFERENCE = 33
DWMWCP_ROUND = 2
```

through `DwmSetWindowAttribute` on the real Overlay HWND.

Microsoft reference:

- https://learn.microsoft.com/windows/apps/desktop/modernize/ui/apply-rounded-corners
- https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwm_window_corner_preference

Keep this in `WindowInterop`, which already owns native Overlay window policy.

Do not create another window-style helper/service.

### 7.2 Failure policy

Rounded corners are presentation-only.

If `DwmSetWindowAttribute` fails:

- log one bounded warning;
- keep the Overlay usable;
- do not fail Show;
- do not affect Runtime capture or controller publication.

### 7.3 Hardware acceptance decides whether a XAML fallback is needed

First use the DWM corner preference only.

If the actual MSI Claw build still renders square corners because of the customized non-client path, use the smallest single-surface XAML fallback necessary in this PR.

Acceptable fallback:

- one outer `Border` / clip owning the same surface;
- one fixed corner radius consistent with Windows 11 UI.

Do not add:

- custom shaped HWND regions;
- per-pixel transparency;
- DirectComposition;
- Acrylic just to hide corners;
- a generalized rounded-window abstraction.

Do not add the fallback unless hardware shows DWM rounding is ineffective.

---

## 8. Neutral gray WinUI-style surface

### 8.1 Do not switch to a full Dark theme

The desired direction is:

```text
neutral gray handheld surface
+ normal WinUI control styling
+ existing accent color
```

not:

```text
full black / dark-mode Overlay
```

### 8.2 There is no separate generic "Gray theme" to enable

WinUI's standard theme families remain Light / Dark / High Contrast.

Do not invent an app-wide third theme.

For this POC:

- keep standard WinUI control templates;
- keep existing WinUI accent resources;
- use a local Overlay-only neutral-gray surface;
- prefer theme resources for normal control foreground, selection, strokes, toggles, sliders, and buttons.

Microsoft theme guidance favors theme resources for controls rather than rebuilding a second color system:

- https://learn.microsoft.com/windows/apps/develop/ui/theming
- https://learn.microsoft.com/windows/apps/get-started/line-of-business/design-for-lob

### 8.3 Surface starting tone

Replace:

```text
#FFF3F3F3
```

with one Overlay-local opaque neutral light gray.

Starting target:

```text
#FFE7E7E7
```

A small hardware-polish adjustment in approximately the `#FFE3E3E3`–`#FFEAEAEA` range is acceptable before merge.

Keep one named Overlay resource, for example:

```xml
<SolidColorBrush x:Key="OverlaySurfaceBrush" Color="#FFE7E7E7" />
```

and use that resource for the primary surface instead of scattering literal gray colors.

Do not create a general app palette.

### 8.4 Keep the Overlay visually Light/Neutral

Because this product decision explicitly does not want a full dark Overlay, it is acceptable to set the Overlay root subtree to the Light requested theme so stock controls remain visually coherent with the neutral light-gray surface even when Windows itself is using Dark mode.

Keep that scope local to `SteamInputAddonforClaw.Overlay`.

Do not change the desktop UI theme policy.

### 8.5 No Mica/Acrylic in this PR

Do not add Mica, Mica Alt, Acrylic, blur, transparency, or desktop-backdrop composition in this POC.

Reasons:

- the Overlay architecture currently requires a simple reliable topmost game/BPM surface;
- the requested visual change is gray tone, not transparency;
- the current one-surface opaque renderer is already proven;
- material/backdrop behavior adds no value to the topmost viability question.

The neutral surface should remain opaque.

---

## 9. Preserve the existing inner UI

Do not use the new space to redesign content yet.

Preserve current:

- five tab identities;
- current tab order;
- LB/RB switching;
- Device/Profile shared Quick Settings renderers;
- Setting tab-order editor;
- Shortcut shell;
- current row selection;
- slider/toggle behavior;
- hidden scrollbar;
- current internal padding unless a tiny adjustment is strictly necessary to prevent obvious layout breakage caused by the new width.

It is expected that the old 400-DIP-oriented content will look sparse in this first large-window POC.

That is acceptable.

The **next** UI architecture work can redesign the large surface after this window geometry/topmost direction is proven.

Do not fill the new area with speculative cards in this PR.

---

## 10. Preserve controller / Runtime behavior exactly

Zero behavior change is allowed to:

- PID1901/PID1902;
- DirectInput ownership;
- HidHide;
- VIIPER server/bus ownership;
- X360 vs SteamDeck presentation selection;
- Steam/BPM observation;
- Overlay capture admission;
- neutral publication;
- release-to-resume gate;
- semantic controller routing;
- WING/OEM1 policy;
- M1/M2;
- rumble;
- gyro;
- TDP;
- fan control;
- Profile authority;
- settings persistence.

In particular, do **not** add another controller reader to `Overlay.exe`.

The existing Runtime semantic navigation remains authoritative.

---

## 11. Preserve topmost / no-activate behavior

Keep:

```text
presenter.IsAlwaysOnTop = true
WS_EX_NOACTIVATE
WS_EX_TOOLWINDOW
HWND_TOPMOST
SWP_NOACTIVATE
Show-time topmost reassertion
WM_MOUSEACTIVATE → MA_NOACTIVATE
```

Do not add:

- `Activate()`;
- `SetForegroundWindow`;
- `BringWindowToTop` as an activation workaround;
- topmost polling;
- a periodic Z-order watchdog;
- focus-proxy windows.

The enlarged Overlay must still appear visually above normal supported desktop/BPM/game surfaces **without becoming the foreground window**.

---

## 12. QAM remains untouched

This PR does not decide the final renderer architecture.

Do not delete or refactor:

- `SteamInputAddonforClaw.QamHost`;
- `qam.js`;
- QAM transport;
- QAM tab injection;
- Steam Quick Access pulse behavior.

The later product decision may retire Addon QAM if the large Overlay proves viable.

That decision comes **after** hardware proof.

---

## 13. Expected files

The implementation should normally remain within:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
src/SteamInputAddonforClaw.Overlay/WindowInterop.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml
src/SteamInputAddonforClaw.Overlay/App.xaml           # only if used for OverlaySurfaceBrush
tests/SteamInputAddonforClaw.Tests/OverlayWindowGeometryTests.cs
relevant existing Overlay source-contract test        # only if useful
```

Touch `OverlayWindow.xaml.cs` only for obsolete geometry diagnostic field names if required.

No Runtime/controller project should need production behavior changes.

If implementation requires broader changes, re-check whether the PR is drifting beyond presentation scope before proceeding.

---

## 14. Geometry tests

Replace the old fixed-width tests.

At minimum cover:

### 14.1 1920×1200 / 150% / bottom taskbar reference

Use a synthetic monitor/work-area matching:

```text
rcMonitor = 0,0 → 1920,1200
rcWork    = 0,0 → 1920,1128
dpi       = 144
```

Expected:

```text
reservedEdgePx      = 72
referenceTaskbarPx  = 72
extraGapPx          = 18
outerMarginPx       = 90

result = X 90
         Y 90
         W 1740
         H 1020
```

### 14.2 Non-zero monitor origin

Verify monitor placement still works for a monitor whose origin is not `0,0`.

### 14.3 Different DPI

Verify the 48-DIP reference and 12-DIP gap scale using the existing DPI rule.

Representative:

```text
96 DPI
120 DPI
144 DPI
168 DPI
192 DPI
```

Do not assert a hard-coded 1920×1200 rectangle for all DPI cases.

### 14.4 Larger-than-reference reserved edge

If a synthetic work area reports a reserved edge thicker than the 48-DIP reference, that real reserved thickness must win before the extra 12-DIP gap is added.

### 14.5 Zero reserved edge

If `rcWork == rcMonitor`, the 48-DIP reference must still preserve the intended large floating margin.

### 14.6 Small monitor/work area

Never return:

- negative width;
- negative height;
- inverted coordinates.

Keep the clamp simple.

Do not add a geometry framework.

---

## 15. Build / static validation

Run:

```text
dotnet build SteamInputAddonforClaw.slnx --no-restore -v:minimal

dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj

git diff --check
```

Existing Overlay/controller/Full1902 tests must remain green.

---

## 16. Required MSI Claw hardware validation

Do not mark hardware-only acceptance as proven from unit tests.

Primary hardware:

```text
1920 × 1200
150% scale
```

### Case A — Windows desktop

```text
1. Keep a normal desktop application foreground.
2. Open Addon Overlay.
3. Confirm the new large floating size.
4. Confirm top/left/right margins feel approximately taskbar-sized plus the small extra gap.
5. Confirm the bottom is above the real taskbar with a visible ~10–20 physical px additional gap.
6. Confirm all four corners are visibly rounded.
7. Confirm the surface is neutral gray, not near-white and not full dark.
8. Confirm foreground application remains foreground.
```

### Case B — Steam BPM only, no game

This is a critical acceptance case for the possible future Main-UI direction.

```text
Steam Big Picture active
no game running
→ invoke Addon Overlay
```

Required:

- enlarged Overlay appears above BPM;
- BPM remains foreground;
- Overlay remains topmost visually;
- controller semantic navigation still operates the Overlay;
- BPM does not receive captured navigation while Overlay capture is active;
- closing Overlay returns immediately to BPM;
- no stuck neutral controller publication.

Confirm existing Show diagnostic still reports conceptually:

```text
TopmostStyle=true
IsOverlayForeground=false
```

### Case C — one representative supported borderless / fullscreen-optimized game

Required:

- Overlay appears above the game;
- game does not minimize;
- game remains foreground;
- enlarged Overlay does not change X360/SteamDeck selection;
- controller is captured by the existing Runtime path;
- closing Overlay returns control cleanly after the existing release gate.

True legacy exclusive fullscreen remains outside the product guarantee for this PR.

### Case D — repeated warm Show/Hide

Repeat at least ten times across desktop or BPM:

```text
Show → Hide → Show
```

Verify:

- same geometry every time;
- no Z-order regression;
- no activation steal;
- no square-corner regression after warm reuse;
- no animation failure;
- no controller leak after close.

---

## 17. Acceptance criteria

Merge only when all are true:

1. The obsolete fixed 400-DIP outer-window width contract is removed.
2. The large Overlay uses one DPI-aware geometry authority.
3. Normal 1920×1200 / 150% hardware leaves approximately one taskbar thickness plus ~18 physical px extra visual margin.
4. The visible Windows taskbar is never intentionally covered.
5. The Overlay has visibly rounded top-level corners on the MSI Claw.
6. The primary surface is neutral light gray rather than `#FFF3F3F3` or full dark.
7. Standard WinUI controls/accent styling remain intact.
8. No Mica/Acrylic/transparency framework is added.
9. Existing topmost/no-activate behavior remains unchanged.
10. Existing Runtime-owned controller capture/navigation/release behavior remains unchanged.
11. BPM-only hardware validation proves the large Overlay can remain visually above Steam Big Picture.
12. A representative supported game validation proves the large Overlay can remain visually above the game without minimizing or activating away from it.
13. QAM is untouched.
14. No new manager, state machine, polling loop, window watchdog, input reader, or generalized layout abstraction is introduced.

---

## 18. Follow-up boundary

If this POC passes the BPM/game acceptance cases, the next design step may evaluate promoting:

```text
SteamInputAddonforClaw.Overlay.exe
→ primary handheld user-facing UI
```

with the desktop UI retained for engineering/diagnostic/setup functions.

Only after that decision should the project consider:

- replacing the current five-tab Quick Settings information architecture;
- moving most desktop user-facing features into Overlay;
- retiring Addon QAM/QamHost integration.

Do not combine those decisions into this presentation POC.
