# Work Order — OQ-ZORDER-A: Restore Reliable Overlay Topmost Persistence

## Status

Focused implementation work order for a real user-visible Overlay window-ordering regression.

Use the label:

```text
OQ-ZORDER-A
```

Do not number this as part of the Full PID1902 controller-authority PR sequence.

Current `main` baseline reviewed for this work order:

```text
b025cd0d5ded23ba5ca01c7f153d43cd0f75480e
```

Observed product symptom:

```text
Addon Quick Settings Overlay becomes visible
→ another ordinary application remains visually above it
→ Overlay is visible behind that application instead of behaving as the topmost Quick Settings surface
```

This is a real supported desktop/windowed lifecycle problem, not a theoretical race.

---

## 1. Required references

Read together before implementation:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/work-order/OQ_POC_A_OVERLAY_WINDOW_VIABILITY_WORK_ORDER.md`
- `docs/work-order/OQ_POC_B_OVERLAY_TRANSPORT_WARM_LIFECYCLE_WORK_ORDER.md`
- `docs/work-order/OQ_ANIM_A_OVERLAY_SHOW_HIDE_ANIMATION_POC_WORK_ORDER.md`
- current `main` implementations of:
  - `src/SteamInputAddonforClaw.Overlay/WindowInterop.cs`
  - `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
  - `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
  - `src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj`

Relevant platform references:

- Win32 `SetWindowPos`: https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos
- Windows App SDK `OverlappedPresenter.IsAlwaysOnTop`: https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.overlappedpresenter.isalwaysontop

The repository currently uses Windows App SDK `2.3.1`; `IsAlwaysOnTop` is available in the supported API lineage.

---

## 2. Existing product contract

The Overlay architecture already requires the top-level surface to be:

```text
borderless
not user-resizable
not shown in taskbar
topmost
WS_EX_NOACTIVATE or equivalent
SWP_NOACTIVATE when shown/positioned
```

Opening the Overlay must not steal foreground activation from the current game/application.

The intended window contract is therefore:

```text
ordinary foreground app/game stays foreground
+
Overlay is visually above ordinary non-topmost windows
+
Overlay remains no-activate
```

Do not solve the current bug by activating the Overlay.

---

## 3. Current code findings

### 3.1 The code already attempts native topmost placement

`WindowInterop.Configure(...)` currently ends with:

```csharp
SetWindowPos(
    hwnd,
    HwndTopmost,
    rect.X,
    rect.Y,
    rect.Width,
    rect.Height,
    SwpNoActivate | SwpNoSendChanging | SwpFrameChanged)
```

`ShowWithoutActivation(...)` also reasserts:

```csharp
SetWindowPos(
    hwnd,
    HwndTopmost,
    0, 0, 0, 0,
    SwpNoActivate | SwpNoSendChanging |
    0x0001 | 0x0002 | 0x0040)
```

Therefore this is **not** a simple case where topmost was never requested.

Keep the Show-time `HWND_TOPMOST` reassertion. It is useful because the Overlay is a warm hidden window reused across many Show/Hide cycles.

### 3.2 Windows App SDK presenter state does not declare the same contract

The current presenter configuration is:

```csharp
if (AppWindow.GetFromWindowId(windowId).Presenter is OverlappedPresenter presenter)
{
    presenter.SetBorderAndTitleBar(false, false);
    presenter.IsResizable = false;
    presenter.IsMaximizable = false;
    presenter.IsMinimizable = false;
}
```

`presenter.IsAlwaysOnTop` is never set.

So the current window has two inconsistent descriptions:

```text
Win32 placement path
→ HWND_TOPMOST requested

Windows App SDK presenter model
→ IsAlwaysOnTop left at its default false
```

The repository should not leave WinUI/AppWindow believing the window is ordinary while native interop separately attempts to keep it topmost.

For this dedicated Overlay window, both layers should express the same intended state.

### 3.3 Hide currently performs a Z-order operation even though Hide should only change visibility

Current code:

```csharp
SetWindowPos(
    hwnd,
    IntPtr.Zero,
    0, 0, 0, 0,
    SwpNoActivate | SwpNoSendChanging |
    0x0001 | 0x0002 | 0x0080)
```

There is no `SWP_NOZORDER`.

Per Win32 contract, `SWP_NOZORDER` is what preserves the current Z-order and ignores `hWndInsertAfter`.

A warm Overlay Hide operation should not modify its Z-order policy at all.

Required conceptual behavior:

```text
Hide
→ visibility changes only
→ existing topmost contract remains intact
```

This missing flag is a real correctness problem in the warm Show/Hide lifecycle even though code inspection alone does not prove it is the sole cause of the observed incident.

### 3.4 Current code gives no direct evidence that topmost state survived

The existing diagnostics log geometry, monitor, DPI, HWND, animation, activation, and bounds, but they do not record the effective topmost state after Configure/Show.

For this specific regression, add one narrow diagnostic so future hardware evidence can answer:

```text
Did the Overlay HWND actually have WS_EX_TOPMOST after Show?
Did the presenter still report IsAlwaysOnTop=true?
```

Do not add a polling monitor or timer.

---

## 4. Required implementation

Keep production changes localized to:

```text
src/SteamInputAddonforClaw.Overlay/WindowInterop.cs
```

Touch `OverlayWindow.xaml.cs` only if strictly necessary for a clean diagnostic call site. No Runtime/controller code should be required.

### 4.1 Set the presenter topmost contract explicitly

Extend the existing presenter configuration:

```csharp
if (AppWindow.GetFromWindowId(windowId).Presenter is OverlappedPresenter presenter)
{
    presenter.SetBorderAndTitleBar(false, false);
    presenter.IsResizable = false;
    presenter.IsMaximizable = false;
    presenter.IsMinimizable = false;
    presenter.IsAlwaysOnTop = true;
}
```

This is not a new authority or manager. `WindowInterop` remains the single owner of Overlay HWND/window-presentation policy.

Do not replace the existing Show-time `HWND_TOPMOST` call with activation or foreground stealing.

### 4.2 Make Hide preserve Z-order

Add named constants instead of keeping the Show/Hide flag values as magic numbers:

```csharp
private const uint SwpNoSize = 0x0001;
private const uint SwpNoMove = 0x0002;
private const uint SwpShowWindow = 0x0040;
private const uint SwpHideWindow = 0x0080;
```

Then change Hide to preserve Z-order explicitly:

```csharp
internal static void Hide(OverlayWindow window)
{
    var hwnd = WindowNative.GetWindowHandle(window);
    if (!SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0, 0, 0, 0,
            SwpNoActivate |
            SwpNoSendChanging |
            SwpNoZOrder |
            SwpNoSize |
            SwpNoMove |
            SwpHideWindow))
    {
        var exception = new Win32Exception(
            Marshal.GetLastWin32Error(),
            "Could not hide the Overlay window.");

        OverlayLog.Error(
            "Window",
            "Overlay hide operation failed.",
            exception,
            ("Operation", "SetWindowPos.Hide"),
            ("OverlayHwnd", hwnd));

        throw exception;
    }
}
```

`hWndInsertAfter` becomes intentionally irrelevant because `SWP_NOZORDER` is present.

### 4.3 Keep Show as a no-activate topmost reassertion

Refactor only the flag names, not the behavior:

```csharp
internal static void ShowWithoutActivation(OverlayWindow window)
{
    var hwnd = WindowNative.GetWindowHandle(window);
    if (!SetWindowPos(
            hwnd,
            HwndTopmost,
            0, 0, 0, 0,
            SwpNoActivate |
            SwpNoSendChanging |
            SwpNoSize |
            SwpNoMove |
            SwpShowWindow))
    {
        ...
    }
}
```

Do **not** add `SWP_NOZORDER` to Show. Show intentionally reasserts `HWND_TOPMOST`.

### 4.4 Add one narrow topmost diagnostic

Add:

```csharp
private const long WsExTopmost = 0x00000008L;
```

After successful final Configure and/or successful Show, inspect the effective extended style using the existing `GetWindowLongPtr` seam.

A reasonable shape is:

```csharp
private static bool HasTopmostStyle(nint hwnd)
{
    var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
    return (exStyle & WsExTopmost) != 0;
}
```

After Show, emit one DEBUG diagnostic similar to:

```text
Overlay topmost state verified.
OverlayHwnd=<...>
TopmostStyle=true
PresenterAlwaysOnTop=true
ForegroundHwnd=<...>
IsOverlayForeground=false
```

If the native topmost style is unexpectedly absent immediately after a successful Show, emit a WARN rather than silently continuing without evidence.

Do not log continuously and do not add a timer.

If reading `presenter.IsAlwaysOnTop` cleanly at the same site would force awkward extra plumbing, logging the native `WS_EX_TOPMOST` state is sufficient. Do not create an abstraction merely for the log.

---

## 5. Preserve no-activate behavior

The following remain prohibited:

```text
Window.Activate()
SetForegroundWindow(...)
SetActiveWindow(...)
BringWindowToTop(...) as an activation workaround
focus-proxy HWND
foreground-stealing retry loop
periodic TopMost timer
Z-order watchdog
```

The bug is that the Overlay is not visually topmost enough in an ordinary supported desktop/windowed scenario.

The fix must **not** convert it into an activated foreground window.

Existing contracts remain:

```text
WS_EX_NOACTIVATE
WM_MOUSEACTIVATE → MA_NOACTIVATE
SWP_NOACTIVATE
logical controller selection/navigation
```

---

## 6. Do not overengineer competing-topmost behavior

The product guarantee for this PR is:

```text
Overlay above ordinary non-topmost desktop applications
Overlay above supported borderless/windowed game surfaces
foreground application remains foreground
```

Do not attempt to guarantee precedence over every other explicit topmost/system surface.

Out of scope:

- secure desktop / UAC desktop;
- shell/system protected surfaces;
- another application intentionally marked always-on-top;
- true legacy exclusive fullscreen;
- injection / swap-chain composition;
- DirectComposition redesign;
- desktop-wide Z-order polling;
- retry loops that repeatedly call `SetWindowPos`;
- new window manager/service/authority abstraction.

If a later reproducible supported-game case proves a normal topmost HWND insufficient, handle that concrete case separately.

---

## 7. Controller / Full1902 scope boundary

This PR must have zero controller-authority behavior change.

Do not modify:

- PID1901/PID1902 policy;
- DirectInput ownership;
- HidHide configuration;
- VIIPER ownership or presentation teardown;
- Xbox360 / SteamDeck selection;
- Overlay neutral publication/capture ownership;
- WING/OEM1 mapping;
- Steam BPM detection;
- QamHost;
- Center M disable/enable lifecycle;
- battery/fan/TDP/profile feature authorities.

This is an Overlay HWND presentation fix only.

---

## 8. Validation

### 8.1 Build and existing tests

Run the normal repository validation required by CI.

Do not build a fake generalized HWND abstraction solely to unit-test P/Invoke flag combinations.

If existing tests can cheaply verify a small extracted pure flag value without production abstraction growth, that is acceptable but not required.

### 8.2 Required hardware/manual validation

Use the actual MSI Claw supported lifecycle.

At minimum verify:

#### Case A — ordinary desktop app

```text
1. Put Explorer / browser / normal WinUI or Win32 app in foreground.
2. Open Addon Overlay.
3. Overlay must appear visually above the application.
4. Foreground application must remain foreground.
```

#### Case B — warm repeated Show/Hide

```text
Show → Hide → Show
```

Repeat at least 10 times.

Expected:

- no cycle leaves the Overlay behind an ordinary application;
- no activation/focus steal;
- no stuck visible/hidden state;
- animation still completes correctly;
- outside-click dismissal still works.

#### Case C — change foreground app while Overlay is hidden

```text
Overlay hidden
→ switch to another ordinary application
→ Show Overlay
```

Expected:

- monitor/WorkArea resolution remains correct;
- Overlay appears over the new foreground application;
- foreground app remains active.

#### Case D — supported game surface

Validate at least one borderless/windowed game on the reference device.

Expected:

- Overlay is visibly above the game;
- game is not minimized;
- game does not lose foreground because of the Overlay Show call;
- closing Overlay restores normal controller publication through the existing capture lifecycle.

True exclusive fullscreen remains outside this PR.

### 8.3 Log evidence

For one successful test capture, confirm that Overlay log shows the effective topmost state after Show.

Expected conceptually:

```text
TopmostStyle=true
PresenterAlwaysOnTop=true    # if logged
IsOverlayForeground=false
```

A normal successful Show with `TopmostStyle=false` is a failure.

---

## 9. Acceptance criteria

Merge only when all are true:

1. `OverlappedPresenter.IsAlwaysOnTop` explicitly matches the product contract.
2. Hide preserves Z-order with `SWP_NOZORDER`.
3. Show continues to reassert `HWND_TOPMOST` with `SWP_NOACTIVATE`.
4. Overlay appears above ordinary applications after first Show and repeated warm Show/Hide cycles.
5. Foreground application remains foreground when Overlay opens.
6. Existing WorkArea/DPI placement, animation, outside-click dismissal, controller capture, and shutdown behavior do not regress.
7. No polling, watchdog, activation workaround, extra manager, or controller-authority change is introduced.

---

## 10. Review note

Do not treat every possible topmost-window ordering interaction as a blocker.

The blocker addressed here is the observed supported-path regression:

```text
ordinary application foreground
→ Overlay requested
→ Overlay visible behind ordinary application
```

That violates the existing Overlay product contract and is realistic normal usage.

Once the simple presenter/native-state alignment and Hide Z-order preservation are implemented and validated, do not add more window-order machinery without new reproducible evidence.
