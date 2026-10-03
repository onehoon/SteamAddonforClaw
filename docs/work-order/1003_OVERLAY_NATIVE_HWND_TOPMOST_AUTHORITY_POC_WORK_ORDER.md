# Work Order — Overlay Native HWND Topmost Authority POC

**Date:** 2026-10-03  
**Status:** Ready for implementation / hardware-validation POC  
**Target repository:** `onehoon/SteamAddonforClaw`  
**Reviewed main baseline:** `cc71e9fa3ee19c12dfc3973e089a72e9bf6701e0`  
**ClawHUD comparison baseline:** `onehoon/ClawHUD@bc7d8a2eb866d3191f8206cf0e6b6a0fb9b34b8b`  
**Implementation shape:** one focused PR  
**Scope:** Overlay top-level HWND visibility/topmost ownership only

---

## 1. Objective

Fix the real MSI Claw production failure where the Addon Overlay process starts, connects, receives `Show`, and successfully executes the current window calls, but the HWND immediately fails the required native topmost postcondition:

```text
PresenterAlwaysOnTop=True
TopmostStyle=False
```

The failure reproduces in Windows Gaming Full Screen Experience / Steam Big Picture and has also been observed intermittently during ordinary Windows desktop use.

The first corrective POC must make the existing Overlay HWND use **one clear visibility/topmost authority** instead of mixing:

```text
WinUI AppWindow visibility
+
OverlappedPresenter topmost policy
+
raw Win32 HWND_TOPMOST promotion
```

The preferred POC is:

```text
WinUI/XAML content remains unchanged
+
existing WinUI Window HWND remains the top-level window
+
native Win32 HWND owns Show / Hide / TOPMOST
```

Do **not** introduce a new native host/XAML-island architecture in this PR. That is a possible later fallback only if this bounded POC still fails on hardware.

---

## 2. Required design authorities

Read and preserve the contracts in:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/work-order/OQ_ZORDER_A_OVERLAY_TOPMOST_PERSISTENCE_WORK_ORDER.md
docs/work-order/0924_OVERLAY_TOPMOST_AND_MAIN_UI_DISPATCH_REGRESSION_FIX_WORK_ORDER.md
```

Current source seams reviewed for this work order:

```text
src/SteamInputAddonforClaw.Overlay/WindowInterop.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
```

Full1902 ownership is not part of this fix.

The Overlay remains only a transient UI surface. This PR must not change:

```text
PID1901 / PID1902
DirectInput ownership
HidHide
VIIPER
SteamDeck / Xbox360 presentation selection
Overlay capture ordering
Steam/BPM detection
Steam FSE registration/configuration
WING/OEM1 routing
```

---

## 3. 2026-10-03 hardware evidence

The `Addon/Log/1003` capture proves that the controller path and Overlay transport reach the expected Show operation.

Representative Runtime flow:

```text
Steam Big Picture session detected
Controller presentation -> SteamDeck

OEM1 Event41 admitted
gesture Single
Front-button action -> QuickSettingsOverlay

Overlay process started
Overlay Ready confirmed
Overlay command requested Command=Show
```

The Overlay process then records:

```text
[Command] Show received.
[Geometry] Overlay geometry applied ...
[Window] Overlay topmost postcondition failed.
TopmostStyle=False
PresenterAlwaysOnTop=True
IsOverlayForeground=False

System.InvalidOperationException:
Overlay was shown without the required topmost window state.
```

This is reproduced by multiple Overlay processes in the capture, including `overlay-5140.log` and `overlay-9736.log`.

A known-good session from the same Addon version also exists in `overlay-13336.log`:

```text
TopmostStyle=True
PresenterAlwaysOnTop=True
Show animation completed
Command Show completed.
```

Therefore:

```text
controller routing is not the failure
button admission is not the failure
Overlay process startup is not the primary failure
named-pipe Show delivery is not the failure

the failing invariant is the Overlay HWND topmost state
```

The user additionally reports that the same missing-Overlay behavior can occur outside FSE. Treat FSE as a strong reproduction environment, not as the root cause.

---

## 4. Important comparison: ClawHUD succeeds in the same FSE environment

ClawHUD currently uses a native Win32 top-level HWND.

Its production window contract in:

```text
onehoon/ClawHUD
src/ClawHUD/HudPresentationContract.h
```

contains:

```cpp
WS_EX_NOACTIVATE |
WS_EX_TOOLWINDOW |
WS_EX_TRANSPARENT |
WS_EX_LAYERED |
WS_EX_TOPMOST
```

Its top-level window is created with `CreateWindowExW(..., WS_POPUP, ...)`.

The native window is then explicitly placed in the topmost band with:

```cpp
SetWindowPos(
    window_,
    HWND_TOPMOST,
    ...,
    SWP_NOACTIVATE | SWP_NOOWNERZORDER);
```

ClawHUD Show uses the same native HWND authority:

```cpp
HRESULT hr = CommitVisibility(true);
if (FAILED(hr))
    return hr;

if (!SetWindowPos(
        window_,
        HWND_TOPMOST,
        0, 0, 0, 0,
        SWP_NOMOVE |
        SWP_NOSIZE |
        SWP_NOACTIVATE |
        SWP_SHOWWINDOW))
{
    return LastErrorResult();
}

ShowWindow(window_, SW_SHOWNOACTIVATE);
```

ClawHUD also had a real fullscreen/screen-filling visibility regression where the HUD remained logically visible but became physically covered.

Its retained production fix reasserts the existing native topmost invariant when `Show()` is requested for an already-visible HUD:

```cpp
if (visible_)
{
    if (!SetWindowPos(
            window_,
            HWND_TOPMOST,
            0, 0, 0, 0,
            SWP_NOMOVE |
            SWP_NOSIZE |
            SWP_NOACTIVATE |
            SWP_NOOWNERZORDER))
    {
        return LastErrorResult();
    }

    return S_OK;
}
```

This comparison is important because ClawHUD is confirmed usable in the Windows Gaming FSE environment where the Addon Overlay currently fails.

Do not copy ClawHUD's renderer or Presentation API. The relevant evidence here is only its **top-level native HWND ownership model**.

---

## 5. Current Addon ownership mismatch

Current `WindowInterop.Configure(...)` performs all of these:

```csharp
WS_EX_NOACTIVATE / WS_EX_TOOLWINDOW mutation
OverlappedPresenter window chrome configuration
presenter.IsAlwaysOnTop = true
native SetWindowPos(... SWP_NOZORDER ...) for geometry
```

Current `ShowWithoutActivation(...)` then performs:

```csharp
AppWindow.Show(false)
SetWindowPos(hwnd, HWND_TOPMOST, ...)
VerifyTopmostStateAfterShow(...)
```

Current `Hide(...)` performs:

```csharp
AppWindow.Hide()
```

So visibility/topmost ownership is currently split across:

```text
AppWindow
OverlappedPresenter
Win32 HWND
```

The 2026-09-24 work order intentionally moved visibility from raw `SWP_SHOWWINDOW/SWP_HIDEWINDOW` to `AppWindow.Show(false)/Hide()` because the mixed visibility path was then the strongest implementation hypothesis.

The 2026-10-03 hardware evidence shows that the resulting implementation still reaches:

```text
AppWindow.Show(false) succeeds
SetWindowPos(HWND_TOPMOST) succeeds
PresenterAlwaysOnTop=True
WS_EX_TOPMOST=False
```

Therefore the 09/24 visibility-owner hypothesis did not resolve the actual hardware failure.

Do not simply add another identical `SetWindowPos(HWND_TOPMOST)` call.

---

## 6. Do not use AppWindow.MoveInZOrderAtTop as the TOPMOST fix

Windows App SDK exposes:

```csharp
AppWindow.MoveInZOrderAtTop()
```

but Microsoft documents this API as corresponding to Win32:

```text
SetWindowPos(..., HWND_TOP, ...)
```

not:

```text
HWND_TOPMOST
```

References:

- https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.moveinzorderattop
- https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos
- https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles

The Overlay contract requires an actual topmost window:

```text
WS_EX_TOPMOST
```

Microsoft explicitly documents that `WS_EX_TOPMOST` should be added or removed with `SetWindowPos`.

Therefore this PR must **not** replace `HWND_TOPMOST` with `MoveInZOrderAtTop()`.

---

## 7. Required implementation

### 7.1 Keep the existing WinUI Window and XAML surface

Do not redesign:

```text
OverlayWindow.xaml
OverlayWindow partial ownership
animation
Quick Settings rendering
outside-click dismissal
controller navigation
named-pipe protocol
Runtime Overlay capture
```

The current `OverlayWindow` remains the XAML owner.

Use its existing HWND from:

```csharp
WindowNative.GetWindowHandle(window)
```

as the sole top-level visibility/topmost authority.

### 7.2 Remove OverlappedPresenter as a topmost authority

Continue using `OverlappedPresenter` for the window chrome constraints already required by the Overlay:

```csharp
presenter.SetBorderAndTitleBar(false, false);
presenter.IsResizable = false;
presenter.IsMaximizable = false;
presenter.IsMinimizable = false;
```

Remove:

```csharp
presenter.IsAlwaysOnTop = true;
```

Do not replace it with another presenter-level topmost mechanism.

After this PR:

```text
Presenter
→ border/titlebar/resizing policy only

native HWND
→ visibility + TOPMOST authority
```

### 7.3 Configure the hidden HWND into the native topmost band

The existing extended-style mutation still owns:

```text
WS_EX_NOACTIVATE
WS_EX_TOOLWINDOW
```

Do not directly add/remove `WS_EX_TOPMOST` with `SetWindowLongPtr`.

Microsoft's documented operation for adding/removing `WS_EX_TOPMOST` is `SetWindowPos`.

Change the final Configure placement from a no-Z-order move:

```csharp
SetWindowPos(
    hwnd,
    IntPtr.Zero,
    rect.X,
    rect.Y,
    rect.Width,
    rect.Height,
    SwpNoActivate |
    SwpNoSendChanging |
    SwpNoZOrder |
    SwpFrameChanged)
```

to one native placement/topmost transaction conceptually equivalent to:

```csharp
SetWindowPos(
    hwnd,
    HwndTopmost,
    rect.X,
    rect.Y,
    rect.Width,
    rect.Height,
    SwpNoActivate |
    SwpNoOwnerZOrder |
    SwpFrameChanged)
```

Add:

```csharp
private const uint SwpNoOwnerZOrder = 0x0200;
```

Do not include `SWP_NOZORDER` in a call whose purpose includes `HWND_TOPMOST`.

Do not carry `SWP_NOSENDCHANGING` into the native topmost-changing transaction. The known-good ClawHUD topmost path does not require it, and the new diagnostic path should be allowed to observe the real window-position lifecycle.

The provisional monitor-sizing call may keep its existing no-Z-order behavior.

### 7.4 Show through the native HWND only

Retire:

```csharp
appWindow.Show(false);
```

from `ShowWithoutActivation(...)`.

Use one native Show/topmost operation:

```csharp
internal static void ShowWithoutActivation(OverlayWindow window)
{
    var hwnd = WindowNative.GetWindowHandle(window);

    if (!SetWindowPos(
            hwnd,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove |
            SwpNoSize |
            SwpNoActivate |
            SwpNoOwnerZOrder |
            SwpShowWindow))
    {
        var exception = new Win32Exception(
            Marshal.GetLastWin32Error(),
            "Could not show the Overlay window in the topmost band.");

        OverlayLog.Error(
            "Window",
            "Overlay native show/topmost transaction failed.",
            exception,
            ("Operation", "SetWindowPos.ShowTopmost"),
            ("OverlayHwnd", hwnd));

        throw exception;
    }

    ShowWindow(hwnd, SwShowNoActivate);

    VerifyTopmostStateAfterShow(hwnd);
}
```

Required constants:

```csharp
private const uint SwpShowWindow = 0x0040;
private const int SwShowNoActivate = 4;
```

The explicit `ShowWindow(..., SW_SHOWNOACTIVATE)` mirrors the already hardware-proven ClawHUD no-activate Show path.

Do not call:

```text
Window.Activate()
AppWindow.Show()
AppWindow.Show(true)
SetForegroundWindow()
SetActiveWindow()
BringWindowToTop()
```

### 7.5 Hide through the same native HWND authority

Retire:

```csharp
AppWindow.Hide()
```

Use:

```csharp
ShowWindow(hwnd, SwHide);
```

with:

```csharp
private const int SwHide = 0;
```

Because `ShowWindow` does not report success/failure through its Boolean return value, verify the required Hide postcondition with `IsWindowVisible(hwnd)`.

Conceptual shape:

```csharp
internal static void Hide(OverlayWindow window)
{
    var hwnd = WindowNative.GetWindowHandle(window);

    ShowWindow(hwnd, SwHide);

    if (IsWindowVisible(hwnd))
    {
        var exception = new InvalidOperationException(
            "Overlay remained visible after the native Hide operation.");

        OverlayLog.Error(
            "Window",
            "Overlay native hide postcondition failed.",
            exception,
            ("OverlayHwnd", hwnd));

        throw exception;
    }
}
```

This preserves the existing Runtime contract that Hide must complete before Overlay capture is retired and game-facing publication resumes.

### 7.6 Keep the topmost Show postcondition fail-closed

Do not weaken or remove:

```text
TopmostStyle=False
→ Show fails
→ Runtime does not commit/retain usable Overlay capture
```

Keep `WS_EX_TOPMOST` readback as the authoritative success condition.

Simplify the verifier so it no longer treats `OverlappedPresenter.IsAlwaysOnTop` as part of the topmost contract.

Preferred fields:

```text
OverlayHwnd
WindowVisible
TopmostStyle
ForegroundHwnd
IsOverlayForeground
```

Required success:

```text
WindowVisible=True
TopmostStyle=True
IsOverlayForeground=False
```

Do not turn foreground equality into a thrown postcondition by itself. The no-activation contract is already enforced by the Show path and should remain observable in logs.

---

## 8. Add narrow ClawHUD-style window diagnostics

The current failure proves that a successful `SetWindowPos(HWND_TOPMOST)` return value alone is not enough evidence.

Add event-driven diagnostics to the existing Overlay HWND subclass. Do not add a polling loop.

### 8.1 WM_WINDOWPOSCHANGED

Observe:

```text
WM_WINDOWPOSCHANGED
```

and log only when the `WINDOWPOS.flags` indicate:

```text
Z-order changed
or
Show/Hide changed
```

Include when available:

```text
OverlayHwnd
hwndInsertAfter
flags
WindowVisible
TopmostStyle
ForegroundHwnd
```

### 8.2 WM_STYLECHANGED for GWL_EXSTYLE

Observe:

```text
WM_STYLECHANGED
wParam == GWL_EXSTYLE
```

Log:

```text
styleOld
styleNew
oldTopmost
newTopmost
```

Use DEBUG-level logging unless an existing repository convention requires otherwise.

The purpose is evidence:

```text
did the native Show transaction establish WS_EX_TOPMOST?
did a later window/presenter/system transition remove it?
```

Do not turn these messages into a state machine or recovery hook.

---

## 9. Tests

Update:

```text
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
```

The current test:

```text
WindowInterop_uses_AppWindow_visibility_and_fails_closed_on_a_missing_topmost_postcondition
```

is now intentionally stale because it explicitly requires:

```text
presenter.IsAlwaysOnTop = true
AppWindow.Show(false)
AppWindow.Hide()
no SWP_SHOWWINDOW
Configure does not use HWND_TOPMOST
```

Replace it with a contract test for the new single native authority.

At minimum assert:

```text
presenter.IsAlwaysOnTop = true is absent
AppWindow.Show(false) is absent
AppWindow.Hide() is absent
MoveInZOrderAtTop is absent

Configure performs a HWND_TOPMOST placement
Configure topmost call does not include SWP_NOZORDER

Show uses HWND_TOPMOST
Show includes SWP_SHOWWINDOW
Show includes SWP_NOACTIVATE
Show includes SWP_NOOWNERZORDER
Show calls ShowWindow(... SW_SHOWNOACTIVATE)
Show verifies WS_EX_TOPMOST after the native show transaction

Hide calls ShowWindow(... SW_HIDE)
Hide verifies IsWindowVisible == false

WS_EX_NOACTIVATE remains
WM_MOUSEACTIVATE -> MA_NOACTIVATE remains

TopmostStyle=False still throws
no Window.Activate()
no SetForegroundWindow()
```

Also add source-wiring assertions for the bounded diagnostics:

```text
WM_WINDOWPOSCHANGED
WM_STYLECHANGED
GWL_EXSTYLE
oldTopmost/newTopmost logging
```

Do not add a fake window manager or an abstraction only for testing.

---

## 10. Files expected to change

Prefer exactly:

```text
src/SteamInputAddonforClaw.Overlay/WindowInterop.cs
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
```

Only touch another file if compilation proves it necessary.

Do not modify:

```text
OverlayWindow.Presentation.cs
App.xaml.cs
Overlay transport protocol
Runtime capture code
SteamFSE code
controller code
Full1902 lifecycle code
XAML layout/resources
```

unless there is a direct compile-time dependency caused by this work.

---

## 11. Explicit non-goals

Do not add:

```text
timer-based TOPMOST reassertion
polling Z-order watchdog
retry loop
sleep/delay + repeated HWND_TOPMOST
foreground stealing
SetForegroundWindow
AttachThreadInput
AllowSetForegroundWindow
new owner HWND hierarchy
new OverlayPresentationManager
new ZOrderManager
new state machine / epoch / barrier
FSE-specific special-case branch
Steam-specific window-class branch
CreateWindowInBand
UIAccess
Game Bar integration
DirectComposition redesign
native XAML host / XAML Island conversion
renderer rewrite
```

Do not modify the Full1902 controller lifecycle to work around a frontend window bug.

---

## 12. Why this POC is preferred over a native-host rewrite

The active Overlay architecture explicitly allows:

```text
WinUI 3 XAML
+
ordinary top-level HWND
+
minimal Win32 window-style/position interop
```

The current `Microsoft.UI.Xaml.Window` already exposes a real top-level HWND.

The immediate defect is not evidence that XAML rendering itself is broken.

It is evidence that the current top-level state is split between:

```text
AppWindow
OverlappedPresenter
raw HWND
```

The smallest evidence-based change is therefore to make the existing HWND the sole visibility/topmost owner while keeping all XAML content unchanged.

A new native host is a materially larger architecture and should not be introduced unless this bounded path still fails on the reference hardware.

---

## 13. Build / automated validation

Run the normal repository build/test flow.

At minimum:

```text
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj
```

and the normal solution/core test validation used by CI.

The PR is not hardware-proven merely because CI passes.

---

## 14. Required MSI Claw hardware validation

Use the current supported MSI Claw reference device.

### Case A — ordinary Windows desktop

```text
1. Keep a normal desktop application foreground.
2. Toggle Addon Overlay open/closed at least 10 times.
3. Confirm every Show is physically visible above the foreground app.
4. Confirm the foreground app remains foreground.
```

Required log shape for every successful Show:

```text
WindowVisible=True
TopmostStyle=True
IsOverlayForeground=False
```

### Case B — ordinary Steam Big Picture

```text
1. Run Steam BPM without Windows Gaming FSE if available.
2. Toggle Overlay at least 10 times.
3. Confirm it remains visually above BPM.
4. Confirm no focus steal.
```

### Case C — Windows Gaming FSE + Steam Big Picture

This is mandatory because it is a current supported product path and a strong reproduction environment.

```text
1. Enable Steam BPM Windows Gaming FSE.
2. Enter BPM as Gaming Home.
3. Confirm SteamDeck presentation remains healthy.
4. Toggle Addon Overlay at least 10 times.
5. Confirm every Show appears above BPM.
6. Confirm BPM remains the foreground application.
7. Confirm controller navigation is captured by the Overlay while visible.
8. Close and confirm the same SteamDeck presentation resumes.
```

### Case D — foreground transition

```text
desktop
→ BPM
→ desktop
→ FSE BPM
→ Overlay Show after each transition
```

No timer/watchdog is expected.

Every explicit Show must establish the native topmost invariant itself.

### Case E — warm hidden process

```text
1. Let Overlay.exe remain warm/hidden.
2. Use Windows/Steam normally for several minutes.
3. Open Overlay.
4. Confirm the existing warm HWND is visible and topmost.
```

---

## 15. Acceptance criteria

Merge only when all are true:

1. The Overlay still uses the existing WinUI/XAML `OverlayWindow`.
2. Native HWND is the sole visibility/topmost authority.
3. `OverlappedPresenter.IsAlwaysOnTop` no longer participates in Overlay topmost ownership.
4. `AppWindow.Show(false)` / `AppWindow.Hide()` are removed from the Overlay presentation path.
5. Native Show uses `HWND_TOPMOST + SWP_SHOWWINDOW + SWP_NOACTIVATE`.
6. Native Hide does not demote or activate the window.
7. Successful Show requires `WS_EX_TOPMOST=True`.
8. Show failure still prevents a usable Overlay/capture success path.
9. No focus stealing is introduced.
10. No timer, retry, watchdog, manager, epoch, or FSE-specific workaround is introduced.
11. Desktop repeated Show/Hide passes.
12. Steam BPM repeated Show/Hide passes.
13. Windows Gaming FSE + Steam BPM repeated Show/Hide passes.
14. No reproduced log contains:

```text
Overlay topmost postcondition failed.
Overlay visible-surface coordination failed.
```

during successful validation.

15. Controller/Full1902 behavior is unchanged.

---

## 16. If the POC still fails

Do not respond by adding retries or repeated topmost calls.

Use the new event-driven diagnostics to determine whether:

```text
A. WS_EX_TOPMOST is never established by the native transaction

or

B. WS_EX_TOPMOST is established and then removed by a later WinUI/system transition
```

If the existing WinUI `Window` HWND still cannot hold the native topmost invariant under the supported product lifecycle after this single-authority conversion, stop this PR and prepare a separate architecture work order for:

```text
native Win32 top-level popup HWND
+
WinUI/XAML content hosting
```

That larger change must not be folded into this POC.

---

## 17. Review policy

Review only realistic supported product behavior.

Blocking examples:

```text
Overlay still intermittently opens behind desktop/BPM
FSE Show still produces TopmostStyle=False
Overlay steals foreground focus
Hide leaves Overlay visible while Runtime resumes game input
Show failure can still commit Overlay capture
warm hidden HWND cannot be reused
native window change breaks pointer interaction or outside-click dismissal
controller presentation changes because Overlay opens/closes
```

Do not block for theoretical instruction-level races with no realistic MSI Claw lifecycle path.

Do not add synchronization/state/manager complexity solely because an artificial timing interleaving can be constructed.

The goal of this PR is narrow:

```text
one existing HWND
one visibility authority
one TOPMOST authority
one deterministic Show/Hide path
```
