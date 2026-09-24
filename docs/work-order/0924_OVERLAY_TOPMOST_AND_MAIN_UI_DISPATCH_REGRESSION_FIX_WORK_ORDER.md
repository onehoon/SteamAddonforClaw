# Work Order — 2026-09-24 Overlay Topmost + Main UI Dispatcher Regression Fix

**Date:** 2026-09-24  
**Status:** Ready for implementation  
**Target repository:** onehoon/SteamAddonforClaw  
**Target branch:** main  
**Reviewed main baseline:** `1292a4fdbcf1b2a427eebd33eb08fcbfad706dca`  
**Implementation shape:** **one PR, exactly two implementation commits**  
**Scope:** WinUI Overlay window presentation + Main UI frontend-notification dispatch only

---

# 0. Required commit structure

Implement this work order as one PR with two focused commits.

Recommended commit order:

```text
Commit 1
Fix overlay topmost postcondition on WinUI visibility lifecycle

Commit 2
Marshal frontend invalidation refreshes to the Main UI dispatcher
```

Do not split these into separate PRs.

Do not combine unrelated cleanup, controller changes, feature work, UI polish, or refactoring into either commit.

---

# 1. Read before implementation

Read current `main` before coding.

At minimum:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/overlayui/README.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/work-order/OQ_ZORDER_A_OVERLAY_TOPMOST_PERSISTENCE_WORK_ORDER.md
```

Inspect current implementations of:

```text
src/SteamInputAddonforClaw.Overlay/WindowInterop.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Presentation.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs

src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/OverlayPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
src/SteamInputAddonforClaw.UI/App.xaml.cs

src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs

tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
```

Product baseline remains:

```text
Standalone Full1902 application
one Windows user
one interactive session
Fast User Switching / RDP / multi-session unsupported
```

CTW integration is not part of this work.

This PR must not change controller authority, PID mode, HidHide, VIIPER, presentation selection, routing, Center M authority, WING/OEM1 behavior, profile semantics, TDP, fan, battery, or Steam detection.

---

# 2. 2026-09-24 hardware evidence

This work order is based on a real supported-device reproduction from the 2026-09-24 Addon logs.

## 2.1 Overlay Topmost failure is confirmed by the Overlay process itself

A known-good Overlay session recorded:

```text
overlay-10252.log

Show count: 2
TopmostStyle=True: 2
TopmostStyle=False: 0
```

The failing long-lived session recorded:

```text
overlay-17968.log

Show count: 30
TopmostStyle=True: 0
TopmostStyle=False: 30
```

Every failing Show followed this shape:

```text
Command Show received
Geometry applied
Overlay window configured
Overlay topmost style is missing after a successful Show.
TopmostStyle=False
IsOverlayForeground=False
Show animation completed
Command Show completed
```

A fresh Overlay process did not automatically recover the invariant:

```text
overlay-11180.log

Show count: 9
TopmostStyle=True: 0
TopmostStyle=False: 9
```

Therefore this is not merely:

```text
one stale Overlay process
or
one missed warm Show/Hide transition
```

The current implementation is able to return a successful Show acknowledgement while its own postcondition says the HWND is not topmost.

That is the primary defect.

## 2.2 Main UI cross-thread failure is also confirmed

`ui-5928.log` recorded 36 `RPC_E_WRONG_THREAD (0x8001010E)` failures in one UI session:

```text
Update refresh:   12
SteamFSE refresh: 12
ClawHUD refresh:  12
```

Representative stacks:

```text
CommunityToolkit.WinUI.Controls.SettingsCard.set_Description
SteamInputAddonforClaw.Views.SettingsPage.RenderAppUpdate
SteamInputAddonforClaw.Views.SettingsPage.RefreshAppUpdateAsync
```

and:

```text
SteamInputAddonforClaw.Views.OverlayPage.RenderClawHud
SteamInputAddonforClaw.Views.OverlayPage.RefreshClawHudAsync
```

At the same time the Runtime continued producing healthy state snapshots.

This is a Main UI dispatch bug, not a Runtime/IPC availability failure.

---

# 3. Important history — do not repeat the old Topmost fix

The historical work order:

```text
docs/work-order/OQ_ZORDER_A_OVERLAY_TOPMOST_PERSISTENCE_WORK_ORDER.md
```

was already implemented.

Relevant completed changes include:

```text
7eba9fac3ada726daa1ceb0cc51a72916411492c
Restore overlay topmost persistence (#520)

9a5dec4ae11d10a8eb3c295687e09f9fe9375385
Fix overlay topmost promotion after show
```

Current `main` already contains all of the following:

```csharp
presenter.IsAlwaysOnTop = true;
```

```csharp
Hide(...)
→ SWP_NOZORDER
```

and the 9/20 split Show path:

```text
1. SetWindowPos(... SWP_SHOWWINDOW + SWP_NOZORDER ...)
2. SetWindowPos(... HWND_TOPMOST ...)
3. read WS_EX_TOPMOST
```

The 2026-09-24 hardware evidence proves that this is still insufficient.

Do not create another PR that merely re-adds:

- `IsAlwaysOnTop = true`;
- `SWP_NOZORDER` on Hide;
- another identical `SetWindowPos(HWND_TOPMOST)`;
- the existing `WS_EX_TOPMOST` diagnostic.

Those already exist.

This work order supersedes the old OQ-ZORDER-A implementation assumptions for the current regression.

---

# 4. Commit 1 — Fix the Overlay topmost postcondition

## 4.1 Current architectural problem

The Overlay is a WinUI 3 `Window` / `AppWindow`, but visibility is currently changed through raw Win32 `SetWindowPos`:

```text
Show
→ SetWindowPos(... SWP_SHOWWINDOW ...)
→ SetWindowPos(... HWND_TOPMOST ...)

Hide
→ SetWindowPos(... SWP_HIDEWINDOW ...)
```

At the same time top-level presentation state is also owned by:

```csharp
OverlappedPresenter.IsAlwaysOnTop = true;
```

This leaves two windowing layers participating in visibility/presentation state:

```text
Windows App SDK AppWindow / OverlappedPresenter
+
raw Win32 SWP_SHOWWINDOW / SWP_HIDEWINDOW
```

The first Topmost fix aligned the presenter policy.

The second fix separated visibility and native promotion.

The hardware log still shows:

```text
SetWindowPos(HWND_TOPMOST) returned success
→ immediate WS_EX_TOPMOST readback = false
→ Show still reported success
```

This evidence **confirms the failed Topmost postcondition**, but it does **not** by itself prove that raw Win32 visibility calls conflicting with WinUI/AppWindow state are the root cause.

Treat the visibility-owner mismatch described below as the current implementation hypothesis to test:

```text
current mixed visibility/presenter path
→ plausible contributor
→ replace with one clearer visibility owner
→ hardware validation decides whether the hypothesis is correct
```

Do not describe the root cause as proven until reference-device validation shows the new path consistently preserves `TopmostStyle=True`.

The next fix should simplify ownership rather than add another retry/watchdog.

## 4.2 Use AppWindow as the visibility owner

Windows App SDK already exposes the exact visibility operations needed by this product:

```csharp
AppWindow.Show(false)
AppWindow.Hide()
```

`Show(false)` explicitly means show without activating the window.

Use these APIs for Overlay visibility rather than `SWP_SHOWWINDOW` / `SWP_HIDEWINDOW`.

Relevant platform references:

- Windows App SDK `AppWindow.Show(Boolean)`
  - https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.show
- Windows App SDK `AppWindow.Hide()`
  - https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.hide
- Win32 `SetWindowPos`
  - https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos

Do not use `Window.Activate()`.

Do not use `AppWindow.Show()` without the Boolean argument.

Required no-activate contract:

```text
AppWindow.Show(false)
+
WS_EX_NOACTIVATE
+
WM_MOUSEACTIVATE → MA_NOACTIVATE
+
native SWP_NOACTIVATE for geometry/topmost placement
```

## 4.3 Keep one bounded native Topmost reassertion after AppWindow.Show(false)

The Overlay must still be above ordinary non-topmost windows and Steam-owned surfaces where the OS permits normal topmost ordering.

Recommended Show sequence:

```text
Configure geometry / presenter
→ AppWindow.Show(false)
→ SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE)
→ verify WS_EX_TOPMOST
→ only then continue Show
```

Conceptual implementation:

```csharp
internal static void ShowWithoutActivation(OverlayWindow window)
{
    var hwnd = WindowNative.GetWindowHandle(window);
    var appWindow = ResolveAppWindow(hwnd);

    if (appWindow.Presenter is OverlappedPresenter presenter)
        presenter.IsAlwaysOnTop = true;

    appWindow.Show(false);

    if (!SetWindowPos(
            hwnd,
            HwndTopmost,
            0, 0, 0, 0,
            SwpNoActivate |
            SwpNoSendChanging |
            SwpNoSize |
            SwpNoMove))
    {
        var exception = new Win32Exception(
            Marshal.GetLastWin32Error(),
            "Could not promote the Overlay window to the topmost band.");

        OverlayLog.Error(
            "Window",
            "Overlay topmost promotion failed.",
            exception,
            ("Operation", "SetWindowPos.ShowTopmost"),
            ("OverlayHwnd", hwnd));

        throw exception;
    }

    VerifyTopmostStateAfterShow(hwnd, presenter);
}
```

The exact helper names may differ.

Do not create an `IWindowManager`, presenter service, retry manager, watchdog, or state machine.

Keep this local to `WindowInterop`.

## 4.4 AppWindow should also own Hide

Replace the raw `SWP_HIDEWINDOW` visibility call with the matching AppWindow API.

Conceptually:

```csharp
internal static void Hide(OverlayWindow window)
{
    var hwnd = WindowNative.GetWindowHandle(window);
    var appWindow = ResolveAppWindow(hwnd);

    try
    {
        appWindow.Hide();
    }
    catch (Exception exception)
    {
        OverlayLog.Error(
            "Window",
            "Overlay hide operation failed.",
            exception,
            ("Operation", "AppWindow.Hide"),
            ("OverlayHwnd", hwnd));

        throw;
    }
}
```

The warm Overlay process remains alive.

This changes only visibility ownership.

It must not close/destroy/recreate the XAML Window for each toggle.

After this change, remove `SwpShowWindow` / `SwpHideWindow` constants if they are no longer used.

## 4.5 Separate geometry from Z-order

`Configure(...)` currently performs final geometry and topmost promotion together:

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

For the new lifecycle, prefer a clearer division:

```text
Configure
→ geometry / frame / no-activate style
→ do not make visibility depend on this call
→ do not use this as the final Show-time topmost guarantee

ShowWithoutActivation
→ visibility
→ topmost promotion
→ topmost verification
```

The final Configure placement should preserve current Z-order:

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

`presenter.IsAlwaysOnTop = true` may remain in presenter configuration because it declares the intended AppWindow policy.

The effective Show postcondition is still verified after the actual Show operation.

## 4.6 Topmost verification must become a real Show postcondition

The current method:

```text
LogTopmostStateAfterShow
```

only warns:

```text
TopmostStyle=False
→ WARN
→ Show continues
→ Runtime receives successful Show acknowledgement
```

That is no longer acceptable because the 09/24 logs prove this state is user-visible.

Change the diagnostic into a verifier, for example:

```csharp
private static void VerifyTopmostStateAfterShow(
    nint hwnd,
    OverlappedPresenter? presenter)
{
    var topmostStyle = HasTopmostStyle(hwnd);
    var presenterTopmost = presenter?.IsAlwaysOnTop;

    ...

    if (!topmostStyle)
    {
        var exception = new InvalidOperationException(
            "Overlay was shown without the required topmost window state.");

        OverlayLog.Error(
            "Window",
            "Overlay topmost postcondition failed.",
            exception,
            fields);

        throw exception;
    }

    OverlayLog.Debug(
        "Window",
        "Overlay topmost state verified.",
        fields);
}
```

Include if practical:

```text
OverlayHwnd
TopmostStyle
PresenterAlwaysOnTop
ForegroundHwnd
IsOverlayForeground
```

The key behavior is:

```text
TopmostStyle=True
→ Show may complete

TopmostStyle=False
→ Show fails
→ do not arm/use the Overlay as if it were valid
```

Preserve the existing Runtime ordering exactly.

Current Host order is:

```text
_overlayController.ShowAsync()
→ require positive Visible acknowledgement
→ presentation.PauseForOverlayAsync(...)
→ require neutral/pause success
→ start OverlayControllerInputRouter
→ _overlayCaptureActive = true
→ capture committed
```

If Show does **not** acknowledge successfully:

```text
OverlayProcessController
→ retires the failed current Overlay session

AddonProcessHost
→ returns immediately
→ does NOT call PauseForOverlayAsync
→ does NOT start OverlayControllerInputRouter
→ does NOT set _overlayCaptureActive
→ current presentation stays live
```

Therefore this is **not a capture rollback path** for a Show failure. Capture/pause has not started yet.

Do not add any new presentation resume/rollback operation for this branch.

Do not add a second failure-recovery path inside the Overlay process.

## 4.7 Do not add retries or foreground stealing

Specifically prohibited:

```text
SetForegroundWindow
Window.Activate
AppWindow.Show(true)
SetActiveWindow
AttachThreadInput
AllowSetForegroundWindow workaround
BringWindowToTop activation workaround
periodic TopMost timer
polling Z-order watchdog
retry loop
sleep/delay + repeated HWND_TOPMOST calls
WM_WINDOWPOSCHANGING interception state machine
new owner HWND hierarchy
```

A single deterministic Show operation plus one native topmost reassertion and immediate postcondition verification is sufficient for this PR.

If that postcondition still fails on the reference device, the PR must not hide the evidence by looping until it happens to pass.

## 4.8 Preserve all existing Overlay behavior

Commit 1 must not regress:

```text
foreground app remains foreground
WorkArea / DPI positioning
1920x1200 @ 150% reference geometry
show/hide animation
outside-click dismissal
logical controller navigation
Overlay capture neutralization
dynamic Shortcut grid
Device/Profile shared renderer
ClawHUD controls
warm Overlay process
Runtime-owned Show/Hide acknowledgement
```

---

# 5. Commit 2 — Marshal Main UI StateInvalidated onto the UI dispatcher

## 5.1 Confirmed code path

`NamedPipeAddonFrontendClient` receives protocol notifications on its read loop and currently raises:

```csharp
StateInvalidated?.Invoke(this, EventArgs.Empty);
```

That transport code is UI-agnostic and should remain so.

The bug is in the Main UI consumer.

Current `MainWindow.OnFrontendStateInvalidated(...)`:

```csharp
private void OnFrontendStateInvalidated(object? sender, EventArgs args)
{
    RequestStatusRefresh();
    SettingsContent.RequestAppUpdateRefresh();
    SettingsContent.RequestSteamFseRefresh();
    OverlayContent.RequestClawHudRefresh();
    ShortcutContent.RequestRefresh();
}
```

Only `RequestStatusRefresh()` explicitly marshals through the MainWindow dispatcher.

`SettingsContent.RequestAppUpdateRefresh()`, `RequestSteamFseRefresh()`, and `OverlayContent.RequestClawHudRefresh()` immediately start async UI work from the event thread.

Their methods use `ConfigureAwait(true)`, but that does not repair the problem because the async operation was entered from the named-pipe read-loop thread, not the UI thread.

That is why the 09/24 log reaches XAML setters from the wrong apartment.

## 5.2 Fix at the one correct boundary

Do not add individual dispatcher wrappers to every page.

Do not make `NamedPipeAddonFrontendClient` depend on WinUI.

For the refresh group owned by `MainWindow.OnFrontendStateInvalidated`, the correct boundary is:

```text
NamedPipe frontend notification
→ MainWindow.OnFrontendStateInvalidated
→ MainWindow.DispatcherQueue
→ MainWindow-owned Settings / SteamFSE / ClawHUD / Shortcut / status refresh requests
```

This statement is intentionally limited to the `MainWindow` subscription path.

`DevicePage` and `ProfilePage` also subscribe directly to `StateInvalidated`, but they already marshal their own callbacks through their existing `DispatcherQueue.TryEnqueue(...)` paths.

Required scope:

```text
MainWindow StateInvalidated subscription
→ fix in this commit

DevicePage direct StateInvalidated subscription
→ keep existing dispatcher path unchanged

ProfilePage direct StateInvalidated subscription
→ keep existing dispatcher path unchanged
```

Do not reroute Device/Profile notifications through MainWindow and do not remove their existing page-local dispatch.

Recommended shape:

```csharp
private void OnFrontendStateInvalidated(object? sender, EventArgs args)
{
    if (DispatcherQueue.HasThreadAccess)
    {
        RefreshInvalidatedFrontendStateOnUiThread();
        return;
    }

    if (!DispatcherQueue.TryEnqueue(RefreshInvalidatedFrontendStateOnUiThread))
    {
        AppLog.Info(
            "Frontend",
            "Frontend state invalidation ignored because the UI dispatcher is unavailable.");
    }
}

private void RefreshInvalidatedFrontendStateOnUiThread()
{
    _ = RefreshSystemStatusAsync();

    SettingsContent.RequestAppUpdateRefresh();
    SettingsContent.RequestSteamFseRefresh();
    OverlayContent.RequestClawHudRefresh();
    ShortcutContent.RequestRefresh();
}
```

Equivalent naming is fine.

The important invariants are:

1. every refresh in the **MainWindow-initiated invalidation group** crosses the MainWindow dispatcher before Settings / SteamFSE / ClawHUD / Shortcut/status UI work starts;
2. DevicePage/ProfilePage keep their existing independent dispatcher-owned invalidation paths;
3. only one new central dispatch boundary is added for the MainWindow group;
4. transport remains UI-agnostic;
5. page render methods remain ordinary UI-thread methods.

## 5.3 Avoid the unnecessary double-dispatch for status

`RequestStatusRefresh()` already uses:

```csharp
DispatcherQueue.TryEnqueue(...)
```

Once `OnFrontendStateInvalidated` itself is marshaled to the UI thread, do not intentionally enqueue status a second time from the new helper.

Prefer:

```csharp
_ = RefreshSystemStatusAsync();
```

inside the UI-thread helper.

Keep `RequestStatusRefresh()` unchanged for its other callers that may legitimately need a dispatching entry point.

## 5.4 Do not add per-page dispatcher infrastructure

Do not add new dispatcher fields or wrappers to:

```text
SettingsPage
OverlayPage
other pages
```

merely for this event path.

`ShortcutPage.RequestRefresh()` already owns its own dispatcher because of its page-local implementation. That is not a reason to duplicate the same pattern everywhere.

The intended architecture for this specific subscription is:

```text
cross-thread frontend event
→ MainWindow subscription
→ marshal once at MainWindow
→ ordinary MainWindow-owned Main UI calls
```

This does not replace the already-correct DevicePage/ProfilePage direct-subscription dispatcher paths.

## 5.5 Do not add refresh epochs / cancellation / coalescing

The 09/24 defect is not evidence of a complicated refresh race.

Do not add:

- refresh epochs;
- invalidation sequence IDs;
- locks;
- semaphores;
- debounce timers;
- cancellation trees;
- a UI refresh manager;
- state-version barriers.

The real production bug is simply that the invalidation callback can run on the named-pipe read-loop thread and directly reaches XAML.

Fix that concrete path.

Normal existing per-feature operation gates may remain as-is.

---

# 6. Tests

## 6.1 Commit 1 tests

Update the existing Overlay window contract test in:

```text
tests/SteamInputAddonforClaw.UiTests/OverlayDeviceRendererWiringTests.cs
```

The test should reflect the new current contract rather than the previous #520 / 9/20 implementation details.

At minimum verify that source wiring contains:

```text
presenter.IsAlwaysOnTop = true
AppWindow.Show(false) or equivalent Show(false) call
AppWindow.Hide()
one Show-time HWND_TOPMOST reassertion
SWP_NOACTIVATE
WS_EX_TOPMOST verification
TopmostStyle=False cannot silently fall through as a successful Show
no Window.Activate()
no SetForegroundWindow()
```

Remove assertions that require raw `SWP_SHOWWINDOW` / `SWP_HIDEWINDOW` if those APIs are intentionally retired.

Also preserve the Runtime capture-order contract with a focused test in `tests/SteamInputAddonforClaw.Tests` (add one if no existing test proves the full branch):

```text
Overlay Show failure / no positive Visible acknowledgement
→ Overlay session retirement remains OverlayProcessController-owned
→ AddonProcessHost returns before PauseForOverlayAsync
→ no OverlayControllerInputRouter start
→ _overlayCaptureActive is never set true
→ presentation remains live
```

This should be a deterministic contract/source-order test. Do not manufacture scheduler timing.

Do not introduce a generalized HWND mock framework solely for this.

## 6.2 Commit 2 tests

Add a focused architecture/source test in:

```text
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
```

Verify that the **MainWindow-owned** `StateInvalidated` path is marshaled through the MainWindow `DispatcherQueue` before its affected refresh requests are invoked.

The test should cover the intended relationship:

```text
MainWindow.OnFrontendStateInvalidated
→ MainWindow DispatcherQueue
→ RefreshSystemStatusAsync
→ RequestAppUpdateRefresh
→ RequestSteamFseRefresh
→ RequestClawHudRefresh
→ Shortcut RequestRefresh
```

Also assert or otherwise preserve that the existing `DevicePage` and `ProfilePage` direct `StateInvalidated` handlers continue to use their own dispatcher enqueue paths and are not rerouted through MainWindow.

Do not test the bug by manufacturing arbitrary thread timing.

This is a deterministic ownership/thread-affinity test.

---

# 7. Required validation

Run the normal repository build/test validation required by CI.

**Do not rely on `SteamInputAddonforClaw.slnx` alone for this work order.**

Current `SteamInputAddonforClaw.slnx` includes:

```text
tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj
```

but does **not** include:

```text
tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj
```

Therefore explicitly rebuild and run the UI test project in both configurations:

```powershell
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release
```

Do not use `--no-build` for these required UI-test runs. The project must be rebuilt so stale UI/Overlay test outputs cannot mask the new source-wiring assertions.

Also run the normal core test project / solution validation so the new Show-failure-before-capture contract test is executed.

Then perform hardware/manual validation on the MSI Claw reference device.

## 7.1 Topmost validation

Use at least:

```text
Case A
normal desktop foreground app
→ Show Overlay
→ Overlay visually above app
→ foreground app stays foreground

Case B
Show → Hide → Show
repeat at least 10 times
→ every Show TopmostStyle=True

Case C
leave Overlay process warm
→ use other apps for a while
→ Show again
→ TopmostStyle=True

Case D
fresh Overlay process after Runtime restart
→ Show
→ TopmostStyle=True

Case E
Steam QAM visible behind where supported
→ Show Addon Overlay
→ Addon Overlay remains above
→ no focus steal
```

The 09/24 regression is not fixed if the log still contains:

```text
Overlay topmost style is missing after a successful Show.
```

A failed topmost postcondition may produce a Show failure, but it must not produce a successful `Command Show completed` / Runtime-visible success path.

## 7.2 Main UI validation

Open the Main UI and exercise state changes that cause Runtime `StateInvalidated` notifications.

At minimum cover:

```text
Settings page / update state
SteamFSE state
Overlay / ClawHUD state
Shortcut state invalidation
normal status refresh
```

Expected:

```text
no COMException 0x8001010E
no RPC_E_WRONG_THREAD
no cross-thread XAML property mutation
UI remains responsive
Runtime IPC remains connected
```

The previous repeated warnings must disappear:

```text
Main UI update state refresh failed.
Main UI SteamFSE state refresh failed.
Main UI ClawHUD state refresh failed.
System.Runtime.InteropServices.COMException (0x8001010E)
```

---

# 8. Explicit non-goals

Do not modify or "improve" the following in this PR:

```text
Center M Enable/Disable
PID1901 / PID1902 transition
HidHide
VIIPER
Xbox360 / SteamDeck routing
controller recovery
rumble
Steam detection
WING/OEM1
suspend/resume
TDP/profile logic
ClawHUD Runtime process ownership
Overlay startup 8-second readiness timeout
UI shutdown fallback Environment.Exit path
```

The 09/24 Center M Enable/Disable test successfully converged and is not part of this fix.

The single observed slow Overlay warm-start timeout is also not part of this PR; the existing fallback recovered and there is not enough evidence to expand scope.

---

# 9. Acceptance criteria

Merge only when all are true.

## Commit 1

- Overlay visibility uses the Windows App SDK `AppWindow.Show(false)` / `Hide()` lifecycle rather than raw `SWP_SHOWWINDOW` / `SWP_HIDEWINDOW`.
- The Overlay remains no-activate.
- Show performs one bounded native `HWND_TOPMOST` reassertion after the visibility operation.
- `WS_EX_TOPMOST` is a required Show postcondition, not warning-only diagnostics.
- A missing Topmost postcondition cannot be acknowledged as a successful usable Overlay.
- No polling, retries, foreground stealing, watchdog, extra manager, or new window authority is introduced.
- Existing Overlay animation, geometry, outside-click, capture, and controller navigation remain unchanged.
- Hardware logs show `TopmostStyle=True` on normal successful Shows.

## Commit 2

- `NamedPipeAddonFrontendClient` remains UI-agnostic.
- `MainWindow.OnFrontendStateInvalidated` marshals only its MainWindow-owned refresh batch to the Main UI dispatcher.
- Update / SteamFSE / ClawHUD rendering no longer runs from the named-pipe read-loop thread through the MainWindow subscription.
- DevicePage/ProfilePage retain their existing direct subscription + dispatcher behavior unchanged.
- Existing page-local feature ownership remains unchanged.
- No new refresh manager, lock, epoch, debounce, or cancellation architecture is added.
- Hardware/UI logs no longer contain the reproduced `RPC_E_WRONG_THREAD (0x8001010E)` refresh failures.

---

# 10. Review policy for this PR

Review against realistic supported product behavior.

Blocking examples:

```text
Overlay can still report Show success with TopmostStyle=False
Overlay steals foreground focus
AppWindow visibility conversion breaks warm Show/Hide
Main UI StateInvalidated can still reach XAML from the pipe thread
UI dispatcher fix drops ordinary notifications during normal app lifetime
existing Overlay capture is left active after a failed Show
```

Do not block for theoretical instruction-level races that have no realistic supported lifecycle path.

Do not add new state/locks/epochs/barriers merely because an artificial thread interleaving can be constructed.

The target is two small ownership corrections:

```text
Overlay visibility owner
→ AppWindow

Main UI cross-thread frontend event boundary
→ MainWindow DispatcherQueue
```

Keep those ownership rules obvious in code.
