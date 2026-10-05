# Work Order — Overlay Full-Monitor Geometry and WorkArea Removal

**Date:** 2026-10-05  
**Status:** Ready for implementation  
**Target repository:** `onehoon/SteamAddonforClaw`  
**Target branch:** `main`  
**Reviewed main baseline:** `11da152f2f3da5ed51487d52bf07b2b3f61693a0`  
**Scope:** standalone WinUI 3 Overlay geometry only  
**Implementation shape:** one small cleanup PR

---

# 0. Goal

Simplify Overlay placement so the window always uses the selected monitor's **full monitor bounds** and intentionally ignores the Windows taskbar/work-area reservation.

The current implementation uses:

~~~text
MONITORINFO.rcMonitor
+
MONITORINFO.rcWork
+
reservedLeft / reservedTop / reservedRight / reservedBottom
+
floating gap
~~~

to keep the Overlay out of the taskbar area.

That policy has two product problems:

1. On the normal MSI Claw layout, the top of the Overlay has only the 4-DIP floating gap while the bottom effectively has `taskbar reservation + 4-DIP gap`, making the panel vertically unbalanced.
2. During games/fullscreen/borderless transitions, the shell can expose different current work-area states, so the same Overlay may sometimes stop above the taskbar and sometimes extend into the taskbar area.

The new product rule is deliberately simpler:

> **Overlay geometry is based only on the selected monitor's full `rcMonitor` bounds. The taskbar/work area is not a geometry authority.**

Target on the reference MSI Claw display:

~~~text
Monitor = 1920 x 1200
DPI     = 144 / 150%
FloatingGapDip = 4
FloatingGapPx  = 6
WidthDip       = 432
WidthPx        = 648

Overlay:
X      = 1266
Y      = 6
Width  = 648
Height = 1188
Bottom = 1194
~~~

Result:

~~~text
6 px
┌──────────────────── Overlay
│
│
│
└────────────────────
6 px
~~~

If the Windows taskbar occupies the bottom, right, left, or top edge, the Overlay is allowed to cover that region.

---

# 1. Mandatory project review

Before implementation, read the current versions of:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md

docs/work-order/OVERLAY_QAM_SHELL_FOUNDATION_PR1_WORK_ORDER_2026-10-02.md
docs/work-order/OVERLAY_QAM_HARDWARE_POLISH_ROW_INSET_TOGGLE_BLUE_WIDTH_WORK_ORDER_2026-10-04.md
~~~

Historical work orders that required taskbar avoidance remain historical records.

This work order supersedes those older geometry decisions for the current product.

Do not modify Full1902 controller ownership or lifecycle behavior.

---

# 2. Mandatory source review

Inspect current main:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
src/SteamInputAddonforClaw.Overlay/WindowInterop.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs

tests/SteamInputAddonforClaw.UiTests/OverlayWindowGeometryTests.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
~~~

Also search current source/tests for:

~~~text
rcWork
workLeft
workTop
workRight
workBottom
WorkWidth
WorkHeight
ReservedLeftPx
ReservedTopPx
ReservedRightPx
ReservedBottomPx
taskbar reservation
taskbar avoidance
~~~

Remove current production/test assumptions that are obsolete under the new geometry policy.

---

# 3. Current implementation facts

## 3.1 Monitor selection remains correct

Current `WindowInterop.Configure(...)` selects the target monitor from the current foreground window:

~~~text
GetForegroundWindow()
-> MonitorFromWindow(... MonitorDefaultToNearest)
-> fallback MonitorFromPoint(... MonitorDefaultToPrimary)
~~~

Keep this behavior.

This work order changes only the rectangle used **inside the selected monitor**.

## 3.2 Provisional full-monitor placement remains useful

Current code first performs provisional placement using:

~~~text
info.rcMonitor.Left
info.rcMonitor.Top
monitorWidth
monitorHeight
~~~

before calling `GetDpiForWindow(hwnd)`.

Keep this two-pass DPI placement.

Do not remove it merely because final geometry also becomes full-monitor based.

## 3.3 Final geometry currently mixes rcMonitor and rcWork

Current call:

~~~csharp
OverlayWindowGeometry.Calculate(
    info.rcMonitor.Left,
    info.rcMonitor.Top,
    info.rcMonitor.Right,
    info.rcMonitor.Bottom,
    info.rcWork.Left,
    info.rcWork.Top,
    info.rcWork.Right,
    info.rcWork.Bottom,
    dpi,
    out var geometry);
~~~

Current `OverlayWindowGeometry.Calculate(...)` derives:

~~~text
reservedLeft
reservedTop
reservedRight
reservedBottom

leftUsable
topUsable
rightUsable
bottomUsable
~~~

from the work area.

That entire reservation layer becomes obsolete.

---

# 4. New geometry contract

Use only:

~~~text
monitorLeft
monitorTop
monitorRight
monitorBottom
dpi
~~~

The geometry formula is:

~~~text
monitorWidth  = max(0, monitorRight - monitorLeft)
monitorHeight = max(0, monitorBottom - monitorTop)

gapPx      = DipToPixels(FloatingGapDip, dpi)
maxWidthPx = DipToPixels(MaxSurfaceWidthDip, dpi)

left   = monitorLeft + gapPx
top    = monitorTop + gapPx
right  = monitorRight - gapPx
bottom = monitorBottom - gapPx

availableWidth = max(0, right - left)
width          = min(availableWidth, maxWidthPx)
x              = max(left, right - width)
height         = max(0, bottom - top)
~~~

Return:

~~~text
OverlayRect(
    X      = x,
    Y      = top,
    Width  = width,
    Height = height)
~~~

No taskbar-related branch is allowed.

No shell/taskbar HWND lookup is needed.

No retry is needed.

No work-area change watcher is needed.

---

# 5. Change A — simplify OverlayWindowGeometry

## 5.1 Remove work-area parameters

Change the calculation signature from the current form:

~~~csharp
Calculate(
    monitorLeft,
    monitorTop,
    monitorRight,
    monitorBottom,
    workLeft,
    workTop,
    workRight,
    workBottom,
    dpi)
~~~

to:

~~~csharp
Calculate(
    monitorLeft,
    monitorTop,
    monitorRight,
    monitorBottom,
    dpi)
~~~

Keep the current `OverlayRect`.

## 5.2 Remove reservation calculations completely

Delete production code for:

~~~text
workWidth
workHeight

reservedLeft
reservedTop
reservedRight
reservedBottom

leftUsable based on reservation
topUsable based on reservation
rightUsable based on reservation
bottomUsable based on reservation
~~~

Replace it with the direct monitor-bound calculation from section 4.

Do not keep dead reservation helpers "for diagnostics" or "for future use."

The product no longer uses them.

## 5.3 Remove OverlayGeometryMetrics if it has no remaining real owner

Current `OverlayGeometryMetrics` exists only to return geometry/debug values such as:

~~~text
MonitorWidth
MonitorHeight
WorkWidth
WorkHeight
ReservedLeftPx
ReservedTopPx
ReservedRightPx
ReservedBottomPx
FloatingGapPx
~~~

After this change most of that type becomes obsolete.

Preferred solution:

> **Delete `OverlayGeometryMetrics` entirely and delete the `out geometry` overload.**

Keep only one `Calculate(...)` overload unless another current production caller genuinely requires a second one.

Do not retain a reduced metrics DTO solely to preserve old logging.

The final `OverlayRect` plus monitor bounds and DPI already provide the useful geometry facts.

---

# 6. Change B — simplify WindowInterop.Configure

## 6.1 Keep GetMonitorInfo, but use rcMonitor only for geometry

`GetMonitorInfo(...)` is still required because `rcMonitor` is the selected monitor bounds.

The Win32 `MONITORINFO` struct must keep its real native layout, including `rcWork`.

Do **not** alter the native struct layout.

However:

~~~text
rcMonitor -> geometry authority
rcWork    -> ignored for placement
~~~

Do not pass `rcWork` to any geometry helper.

## 6.2 Final calculation

Target call:

~~~csharp
rect = OverlayWindowGeometry.Calculate(
    info.rcMonitor.Left,
    info.rcMonitor.Top,
    info.rcMonitor.Right,
    info.rcMonitor.Bottom,
    dpi);
~~~

Keep the existing final:

~~~text
SetWindowPos(
    hwnd,
    HWND_TOPMOST,
    rect.X,
    rect.Y,
    rect.Width,
    rect.Height,
    SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_FRAMECHANGED)
~~~

The topmost/no-activate policy is unchanged.

## 6.3 Delete obsolete work-area diagnostics

Current `Overlay geometry applied` logging includes:

~~~text
WorkLeft
WorkTop
WorkRight
WorkBottom
WorkWidth
WorkHeight
ReservedLeftPx
ReservedTopPx
ReservedRightPx
ReservedBottomPx
FloatingGapPx
~~~

Remove the obsolete work-area/reservation fields.

Keep useful facts such as:

~~~text
OverlayHwnd
ForegroundHwnd
MonitorLeft
MonitorTop
MonitorRight
MonitorBottom
Dpi
Scale
MonitorWidth
MonitorHeight
OverlayWidthPx
OverlayHeightPx
~~~

`MonitorWidth` and `MonitorHeight` may use the already-calculated local values in `Configure(...)`.

Do not introduce a new diagnostics object just to keep the old log shape.

---

# 7. Change C — do not add taskbar-specific replacement logic

This PR intentionally removes taskbar geometry authority.

Do **not** replace `rcWork` with:

~~~text
FindWindow("Shell_TrayWnd")
FindWindow("Shell_SecondaryTrayWnd")
SHAppBarMessage(ABM_GETTASKBARPOS)
ABN_POSCHANGED
WM_SETTINGCHANGE
SPI_GETWORKAREA
taskbar-height hardcoding
taskbar auto-hide probing
Explorer polling
post-show taskbar retry
delayed geometry correction
~~~

Do not add:

~~~text
taskbar manager
work-area watcher
geometry epoch
retry loop
timer
shell-state state machine
~~~

The purpose of this change is to eliminate that dependency entirely.

---

# 8. Change D — update active architecture wording

Update current active architecture documentation where it still states that Overlay geometry uses the Windows WorkArea/taskbar reservation.

At minimum inspect and update:

~~~text
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
~~~

Current product wording should become conceptually:

~~~text
Overlay final geometry uses the selected monitor's full monitor bounds.

X      = Monitor.Right - FloatingGap - Width
Y      = Monitor.Top + FloatingGap
Width  = min(432 DIP, monitor width minus both floating gaps)
Height = Monitor.Height - both floating gaps

The Windows taskbar/work area is intentionally ignored.
The Overlay is allowed to cover the taskbar because it is a transient,
topmost, no-activate Quick Settings surface rather than an ordinary desktop window.
~~~

Do not rewrite historical work orders.

Historical documents may still say:

~~~text
taskbar avoidance
rcWork
WorkArea
~~~

That is acceptable as history.

The new active architecture and this work order supersede them.

---

# 9. Tests

## 9.1 Rewrite OverlayWindowGeometryTests around the new authority

Remove tests whose only purpose is proving taskbar reservation behavior:

~~~text
UsesRightSide432DipSurfaceWithIndependentBottomTaskbarReservation
RightTaskbarReservationMovesTheSurfaceLeftWithoutChangingTopOrBottomInsets
LeftTaskbarReservationIsAppliedOnlyToTheLeftUsableBound
TopTaskbarReservationIsAppliedOnlyToTheTopUsableBound
BottomTaskbarReservationIsAppliedOnlyToTheBottomUsableBound
UsesFloatingGapWhenWorkAreaHasNoReservedEdge
~~~

Replace with direct full-monitor geometry tests.

Required reference test:

~~~csharp
var result = OverlayWindowGeometry.Calculate(
    0, 0, 1920, 1200,
    144);

Assert.Equal(
    new OverlayRect(1266, 6, 648, 1188),
    result);
~~~

Keep or adapt:

- DPI scaling theory for 96/120/144/168/192;
- non-zero monitor origin;
- adjacent-points/outside bounds behavior;
- unusually small monitor clamp;
- zero-sized monitor behavior.

The existing DPI expected heights for a full 1920x1200 monitor remain:

~~~text
96 DPI  -> gap 4  -> height 1192
120 DPI -> gap 5  -> height 1190
144 DPI -> gap 6  -> height 1188
168 DPI -> gap 7  -> height 1186
192 DPI -> gap 8  -> height 1184
~~~

## 9.2 Add a source-contract regression against reintroducing WorkArea

In the most appropriate current Overlay UI source-contract test, assert that production geometry no longer references:

~~~text
info.rcWork
ReservedLeftPx
ReservedTopPx
ReservedRightPx
ReservedBottomPx
WorkWidth
WorkHeight
~~~

in the final geometry calculation/logging path.

Do not write a broad repository-wide test that fails because historical documentation still contains `rcWork`.

Scope the assertion to production source files.

## 9.3 Keep current placement/topmost tests

Do not weaken tests proving:

~~~text
MonitorFromWindow foreground monitor selection
GetDpiForWindow two-pass placement
final SetWindowPos uses HWND_TOPMOST
SWP_NOACTIVATE
SWP_FRAMECHANGED
ShowWithoutActivation topmost reassertion
~~~

This PR changes geometry bounds, not window lifecycle/Z-order policy.

---

# 10. Manual hardware validation

Primary target:

~~~text
MSI Claw
1920 x 1200
150% / 144 DPI
~~~

Validate all of the following.

## Desktop

With normal visible bottom taskbar:

~~~text
Overlay top gap    ~= 6 px
Overlay bottom gap ~= 6 px to physical display edge
Overlay covers taskbar area
right gap          ~= 6 px
~~~

The old ~72px taskbar reservation must not remain.

## Game / borderless

Open/close Overlay repeatedly while a game is active.

Expected:

~~~text
every show -> same monitor-bound geometry
~~~

The bottom edge must not alternate between:

~~~text
taskbar-reserved height
full-monitor height
~~~

because `rcWork` is no longer consumed.

## Fullscreen transitions

Where supported by the game, test after:

~~~text
desktop -> game
game windowed -> borderless/fullscreen
Alt-Tab out/in
game -> desktop
~~~

Expected:

- current foreground monitor selection still works;
- geometry remains monitor-bound;
- no taskbar/work-area-dependent height change;
- Overlay remains topmost/no-activate.

## Taskbar positions

No dedicated support logic is needed, but if convenient verify that top/left/right taskbar placement does not move or shrink the Overlay.

The taskbar is intentionally ignored.

---

# 11. Lifecycle and authority non-goals

Do not change:

- Full1902 authority;
- Center M Enabled/Disabled policy;
- PID1901/PID1902 restoration;
- HidHide;
- VIIPER;
- DirectInput;
- physical device re-enumeration;
- Sleep/Hibernate/Resume;
- restart/crash/shutdown;
- routing rollback/fail-close;
- X360/SteamDeck presentation;
- Overlay capture/release;
- Runtime dismiss authority;
- Overlay process lifetime;
- outside-click dismissal;
- show/hide animation timing;
- page rendering;
- controller navigation;
- feature mutations.

This is geometry simplification only.

---

# 12. Expected production-file scope

Expected production changes should remain primarily within:

~~~text
src/SteamInputAddonforClaw.Overlay/OverlayWindowGeometry.cs
src/SteamInputAddonforClaw.Overlay/WindowInterop.cs
~~~

Expected test changes:

~~~text
tests/SteamInputAddonforClaw.UiTests/OverlayWindowGeometryTests.cs
possibly one existing source-contract Overlay UI test
~~~

Expected active-doc updates:

~~~text
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
~~~

`OverlayWindow.Presentation.cs` should normally require no change.

If the implementation starts adding taskbar APIs, timers, retries, shell watchers, or another geometry abstraction, stop and simplify.

---

# 13. Acceptance criteria

The PR is complete when all of the following are true:

1. Final Overlay geometry uses only selected-monitor `rcMonitor` bounds plus DPI.
2. `rcWork` is not used for placement.
3. WorkArea parameters are removed from `OverlayWindowGeometry.Calculate(...)`.
4. `reservedLeft/Top/Right/Bottom` calculations are deleted.
5. Obsolete WorkArea/reservation geometry metrics are deleted.
6. `OverlayGeometryMetrics` is removed if no genuine production owner remains.
7. Obsolete WorkArea/reservation log fields are removed.
8. No taskbar-specific replacement API is added.
9. No retry/timer/watcher/state machine is added.
10. 432-DIP width remains unchanged.
11. 4-DIP physical monitor-edge gap remains unchanged.
12. Reference 1920x1200 @ 150% geometry is `1266,6,648,1188`.
13. Top and bottom physical monitor-edge gaps are symmetric.
14. Overlay is allowed to cover the taskbar.
15. Foreground-monitor selection remains unchanged.
16. Two-pass DPI placement remains unchanged.
17. Final and show-time HWND_TOPMOST behavior remains unchanged.
18. No-activate behavior remains unchanged.
19. Existing small/zero monitor safety remains.
20. Current active Overlay architecture docs describe full-monitor geometry and intentional taskbar overlap.
21. Build and tests pass.
22. Hardware validation shows no random taskbar-reserved vs full-monitor Overlay height while gaming.

---

# 14. Implementation principle

The old model was:

~~~text
monitor bounds
+ Windows work-area state
+ per-edge reservations
+ floating gap
-> Overlay rect
~~~

The new model is intentionally:

~~~text
monitor bounds
+ DPI-scaled floating gap
+ fixed max width
-> Overlay rect
~~~

This is not a fallback path.

It is the single product geometry authority.

Delete the obsolete WorkArea logic rather than preserving two policies behind conditionals.
