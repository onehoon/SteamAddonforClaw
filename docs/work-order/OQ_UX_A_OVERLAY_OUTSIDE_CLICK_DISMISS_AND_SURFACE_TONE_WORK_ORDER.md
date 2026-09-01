# Work Order — OQ-UX-A: Overlay Outside-Click Dismiss and Surface Tone

## Status

Implementation work order for the next focused Addon Quick Settings Overlay UX PR after the merged warm-lifecycle, logging, animation, and PR443 corrective work.

Use the label:

```text
OQ-UX-A
```

Do **not** number this as part of the Full PID1902 controller-authority PR sequence.

Current `main` baseline when this work order was prepared:

```text
2b5544b626392f0ba4c202ac865d5b7272384cce
Add PR8 owned DirectInput session recovery work order
```

Relevant merged Overlay baseline:

```text
OQ-POC-A
→ standalone WinUI3 Overlay window viability

OQ-POC-B / PR #436
→ Runtime-owned warm hidden Overlay.exe
→ dedicated .Overlay transport
→ Show / Hide / Shutdown
→ Ready / Visible / Hidden

OQ-LOG-A / PR #438
→ persistent Overlay lifecycle/geometry/timing diagnostics

OQ-ANIM-A / PR #440
→ compositor Show/Hide animation

OQ-ANIM-A-FIX / PR #443
→ merge commit 8934a3105f0cdb12aabd154b6c5832d8ddb5018c
→ remove redundant startup Hidden frame
→ Ready establishes canonical hidden session
→ fresh Show acknowledgement fixed
→ stationary opaque panel
→ inner-content-only 32 DIP motion
→ obsolete Close POC button removed
→ no-activate mouse/window diagnostics added
```

Before implementation, read and preserve these design authorities:

- `docs/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ_POC_A_OVERLAY_WINDOW_VIABILITY_WORK_ORDER.md`
- `docs/work-order/OQ_POC_B_OVERLAY_TRANSPORT_WARM_LIFECYCLE_WORK_ORDER.md`
- `docs/work-order/OQ_LOG_A_OVERLAY_POC_DIAGNOSTIC_LOGGING_WORK_ORDER.md`
- `docs/work-order/OQ_ANIM_A_OVERLAY_SHOW_HIDE_ANIMATION_POC_WORK_ORDER.md`
- current `main` implementations of:
  - `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml`
  - `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
  - `src/SteamInputAddonforClaw.Overlay/WindowInterop.cs`
  - `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
  - `src/SteamInputAddonforClaw.Overlay/Diagnostics/OverlayLog.cs`
  - `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
  - `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`
  - `tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs`

The application is pre-release. Do not add compatibility layers for the old POC behavior.

---

## 1. Goal

Apply the next small UX correction proven necessary by real MSI Claw hardware testing:

1. Change the current nearly-black/blue-tinted Overlay panel surface to a neutral Windows 11 Settings-like dark gray.
2. Make the Overlay behave as a transient Quick Settings surface when using a mouse:
   - clicks inside the Overlay keep it open;
   - clicks outside the Overlay request dismissal;
   - the outside click itself continues to the underlying desktop/game/application;
   - Runtime remains the one owner of Overlay visibility transitions.
3. Improve the existing low-rate activation diagnostics enough to determine whether the no-activate contract is actually violated on future hardware/game tests.

Desired UX:

```text
Overlay hidden
    ↓
Runtime Show
    ↓
Overlay visible on left
    ↓

click inside Overlay
→ Overlay remains visible

click outside Overlay
→ Overlay sends DismissRequested
→ Runtime sends normal Hide command
→ 150 ms Hide animation
→ Hidden acknowledgement
→ same Overlay.exe remains warm
```

This PR is intentionally narrow.

It must **not** implement controller capture, physical button handling, production Quick Settings controls, Steam-QAM coexistence policy, or Game Bar policy.

---

## 2. Hardware evidence that motivates this PR

Latest real-hardware validation was performed on the MSI Claw desktop at:

```text
Display      = 1920 × 1200
Scale        = 150%
DPI          = 144
WorkArea     = 1920 × 1128
Overlay      = 400 DIP / 600 physical px × 1128 px
Overlay PID  = 6340
Overlay HWND = 983676
```

The PR443 fixes were successful in the important lifecycle paths:

```text
5 repeated Show operations
5 repeated Hide operations
same PID
same HWND
no pipe reconnect
no 0–1 ms false Show acknowledgement
no broken-pipe failure
no Overlay WARN/ERROR during normal Show/Hide
clean Runtime Shutdown → Overlay graceful exit
```

Observed command timing was stable:

```text
Show Runtime→ACK ≈ 206–266 ms
Hide Runtime→ACK ≈ 167–178 ms
```

The two remaining UX observations are different from the fixed PR443 failures.

### 2.1 Dark surface

Only the Overlay panel region is dark; the entire desktop is not covered.

Current XAML explicitly defines:

```xml
<Grid x:Name="OpaquePanel"
      Background="#FF20242B"
      Padding="28">
```

Therefore the remaining dark appearance is the intended opaque panel surface itself, not evidence of another full-screen or backing-surface window.

### 2.2 Outside click does not dismiss

The current implementation has no outside-click dismissal path.

The Overlay intentionally uses:

```text
WS_EX_NOACTIVATE
SWP_NOACTIVATE
WM_MOUSEACTIVATE → MA_NOACTIVATE
```

Therefore a conventional activated-window pattern such as:

```text
Window activated
→ LostFocus / Deactivated
→ Hide
```

is not an appropriate authority or detection mechanism.

The Overlay should remain no-activate and still detect a user mouse click outside its own rectangle.

### 2.3 Activation diagnostic note

One hardware log contained:

```text
WM_ACTIVATE State=1
```

The test was performed on the Windows desktop and the Overlay was opened through the Runtime tray POC menu, not from a foreground game.

That single line is not sufficient evidence to redesign no-activate behavior.

Do **not** add focus proxies, activation restoration, foreground stealing, or additional window ownership machinery in this PR.

Only improve diagnostics enough to determine the actual foreground HWND when such a message occurs again.

---

## 3. Central authority rule

The most important rule in this PR is:

> **Overlay.exe may detect that the user clicked outside, but Runtime remains the owner that decides and executes the Hide transition.**

Do not implement:

```text
outside click
→ Overlay.exe calls WindowInterop.Hide() locally
→ Runtime still thinks _visible == true
```

That creates two visibility authorities and makes the next Toggle ambiguous.

Correct direction:

```text
outside click detected by Overlay.exe
        ↓
Overlay → Runtime: DismissRequested
        ↓
OverlayProcessController
        ↓
existing Runtime Hide command path
        ↓
Overlay HideForPocAsync()
        ↓
Hidden state
        ↓
Runtime _visible = false
```

This also preserves the correct seam for future OQ4 controller capture/release ordering.

When capture is later implemented, Runtime must be able to coordinate:

```text
close request
→ Overlay Hide
→ held-control release gate
→ clear OverlayCapture
→ resume the same presentation
```

Do not create a local Overlay-only Hide authority now that would have to be removed later.

---

## 4. Surface tone change

Change the current panel background from:

```text
#FF20242B
```

to:

```text
#FF2B2B2B
```

Target XAML:

```xml
<Grid x:Name="OpaquePanel"
      Background="#FF2B2B2B"
      Padding="28">
```

Intent:

```text
old #20242B
→ very dark blue-gray / nearly black

new #2B2B2B
→ neutral Windows 11 Settings-like dark gray
```

Keep the panel fully opaque.

Do not add in this PR:

- Acrylic;
- Mica;
- blur;
- transparency;
- alpha animation on the panel background;
- gradient;
- dynamic background capture;
- theme resource abstraction solely for this one POC color.

The current content-only opacity animation remains unchanged.

`AnimatedContent` may continue to animate:

```text
Show: opacity 0.90 → 1.00
Hide: opacity 1.00 → 0.90
```

The stationary `OpaquePanel` itself remains fully opaque at all times while visible.

---

## 5. Preserve the PR443 animation/window structure

Do not undo the real PR443 backing-surface fix.

Required hierarchy remains conceptually:

```text
AnimationViewport
└─ OpaquePanel              ← stationary, full HWND area
   └─ AnimatedContent       ← only this content moves/fades
```

Keep:

```text
ContentSlideDistanceDip = 32
ShowDuration            = 180 ms
HideDuration            = 150 ms
Show easing             = ease-out
Hide easing             = ease-in
```

Do not return to:

```text
400 DIP full-panel translation
```

Do not animate the HWND position per frame.

Do not change WorkArea/DPI geometry.

---

## 6. Required outside-click behavior

When the Overlay is visible:

### Inside click

```text
pointer button down
+ screen point is inside current Overlay HWND rectangle
→ do nothing
→ click is delivered normally to XAML
→ Overlay remains visible
```

This includes:

- actual controls;
- text;
- blank padding/background inside the 600px-wide panel;
- future slider/toggle/card areas.

### Outside click

```text
pointer button down
+ screen point is outside current Overlay HWND rectangle
→ request one DismissRequested event
→ do not consume the click
→ underlying desktop/game/application still receives the click
→ Runtime drives normal Hide
```

Treat these mouse button-down events as dismissal triggers:

```text
WM_LBUTTONDOWN
WM_RBUTTONDOWN
WM_MBUTTONDOWN
WM_XBUTTONDOWN
```

Do not dismiss on:

- mouse move;
- wheel movement alone;
- hover;
- cursor leaving the Overlay;
- focus/foreground changes by themselves.

A drag that starts inside the Overlay should not be reinterpreted as an outside-click dismissal merely because the pointer later moves outside while held.

This matters for future sliders.

The dismissal decision should therefore be based on the **button-down point**, not later mouse movement.

---

## 7. Detection mechanism — narrow visible-lifetime `WH_MOUSE_LL`

Because the Overlay is intentionally no-activate, do not depend on normal XAML/Win32 focus-loss events to detect clicks in another process/window.

For this focused POC, use the smallest reliable Win32 seam:

```text
WH_MOUSE_LL
```

owned by the Overlay process and active **only while the Overlay is visible**.

Preferred direct location:

```text
WindowInterop
```

Do not add a generalized global-input service or manager.

### Why the hook is acceptable here

The hook is not being used to capture controller input or globally remap the mouse.

It only answers one transient UI question:

> Did a mouse button go down outside the visible Overlay rectangle?

The hook must remain inactive while the Overlay is hidden.

### Required lifetime

```text
Overlay hidden
→ no mouse hook

Show reaches visible path
→ install outside-click hook

Hide begins
→ stop accepting outside-click dismissal
→ unhook
→ run normal Hide animation/window hide

Shutdown / WM_NCDESTROY
→ ensure hook removed
```

Do not leave `WH_MOUSE_LL` installed for the entire warm process lifetime.

### Callback performance

The low-level callback must be minimal.

For normal mouse-move events:

```text
inspect message id
→ no allocation-heavy work
→ no log
→ CallNextHookEx
```

Only on supported button-down messages:

```text
read MSLLHOOKSTRUCT.pt
→ GetWindowRect(OverlayHwnd)
→ point-in-rect test
→ if outside, signal one dismiss request
→ CallNextHookEx
```

Do not perform named-pipe I/O synchronously inside the unmanaged hook callback.

Do not await anything from the hook callback.

Do not block the Windows input path.

---

## 8. Do not consume the outside click

Outside-click dismissal must behave like a normal transient popup/panel.

Example:

```text
Overlay visible
→ user clicks a desktop icon outside panel
→ Overlay begins closing
→ the desktop click still reaches Explorer
```

The hook callback must return through normal propagation, conceptually:

```csharp
return CallNextHookEx(...);
```

Do not return a value that swallows the click.

Do not use:

```text
WS_EX_TRANSPARENT
```

The Overlay must remain interactable inside its own rectangle.

Do not add a full-screen transparent catcher window behind the panel.

That would create another top-level surface, interfere with underlying input, and unnecessarily complicate z-order/focus.

---

## 9. One dismiss signal per visible session

A user may click several times while the 150 ms Hide path is in progress.

Do not emit an unbounded stream of `DismissRequested` frames.

Keep the solution local and small.

A simple per-visible-show gate is enough, conceptually:

```text
Show
→ dismissSignaled = false

first outside button down
→ atomically/locally mark dismissSignaled = true
→ raise one dismissal request

further outside clicks before Hide
→ do not raise more requests

next Show
→ dismissSignaled = false again
```

This does **not** justify:

- request IDs;
- epochs;
- generations;
- command correlation managers;
- a new state machine.

The gate only prevents duplicate UI requests during one obvious visible interval.

---

## 10. Overlay-side callback shape

Keep Win32 detection and application transport responsibilities separate but local.

Preferred conceptual flow:

```text
WindowInterop WH_MOUSE_LL callback
→ outside button-down proven
→ invoke a small managed callback/event
→ return immediately to CallNextHookEx

OverlayWindow/App callback
→ asynchronously send DismissRequested on .Overlay
```

Do not write to the pipe directly inside the native hook callback.

A small event such as:

```text
OverlayWindow.OutsideClickDismissRequested
```

or equivalent is acceptable.

Do not create:

- `OverlayDismissManager`;
- `MouseCaptureService`;
- `GlobalInputRouter`;
- generic event bus.

The existing Overlay has one window and one purpose.

---

## 11. Extend the `.Overlay` wire narrowly

The existing wire currently has:

```csharp
OverlayWireMessageKind
{
    Handshake,
    HandshakeAccepted,
    Command,
    State,
    ProtocolError
}
```

Add one explicit client→Runtime message kind:

```text
DismissRequested
```

This is preferable to abusing `OverlayState.Hidden` as an unsolicited dismissal event.

Why:

```text
Hidden
= a real resulting window state / acknowledgement

DismissRequested
= user intent asking Runtime to perform the normal Hide transition
```

Keep those meanings separate.

Do not add a generalized event taxonomy.

Do not add a payload unless actually needed.

A compact frame is enough:

```text
Kind = DismissRequested
ProtocolVersion = current
Command = null
State = null
Error = null
```

---

## 12. Overlay protocol version

Unlike PR443, this PR changes the `.Overlay` wire contract by adding a new message kind.

Therefore bump only the Overlay protocol:

```text
OverlayTransportProtocol.CurrentVersion
1 → 2
```

Do **not** change:

```text
FrontendTransportProtocol.CurrentVersion
```

The desktop `.Frontend` and Steam `.Qam` protocols are unrelated and remain unchanged.

Runtime and Overlay are shipped as one product payload, but retaining a truthful Overlay protocol version still makes mixed/stale staged binaries fail explicitly rather than being misinterpreted.

Do not add backward-compatibility parsing for Overlay protocol v1.

The product is pre-release and the new Overlay surface is not a compatibility contract.

---

## 13. NamedPipeOverlayClient change

Add one narrow client method, conceptually:

```csharp
Task SendDismissRequestedAsync(...)
```

It should reuse:

- the existing connected `_pipe`;
- existing `_writeGate`;
- existing bounded `OverlayWireCodec` framing;
- existing process-lifetime cancellation.

Do not create a second pipe.

Do not reconnect solely to send dismiss.

Do not use a new serializer.

If sending the dismissal request fails:

```text
log the transport failure
→ do not locally hide the Overlay behind Runtime's back
→ existing pipe/process failure policy remains authoritative
```

A failed dismissal request is feature-local; it must not touch controller ownership or routing.

---

## 14. NamedPipeOverlayServer change

The server receive loop must accept two categories after handshake:

```text
State
DismissRequested
```

Existing State semantics remain:

```text
Ready
→ canonical hidden usable session

Visible
→ Show result

Hidden
→ Hide result
```

`DismissRequested` is not a state and must not complete a Show/Hide acknowledgement by itself.

Expose one narrow notification to the Runtime-side lifecycle owner, for example:

```text
DismissRequested event
```

Exact naming may follow repository style.

### Critical receive-loop rule

Do **not** synchronously await the Runtime Hide operation from inside `NamedPipeOverlayServer.ServeAsync()`.

Wrong:

```text
ServeAsync reads DismissRequested
→ await Runtime hide handler
→ Runtime sends Hide and waits for Hidden
→ ServeAsync is still blocked and cannot read Hidden
→ deadlock / timeout
```

Correct:

```text
ServeAsync reads DismissRequested
→ raise/schedule a non-blocking notification
→ immediately continue receive loop

Runtime handler runs independently
→ sends Hide
→ client handles Hide
→ client sends Hidden
→ ServeAsync is free to read Hidden
→ command acknowledgement completes
```

This is a direct consequence of the current single receive loop, not a reason to add another transport architecture.

---

## 15. OverlayProcessController remains visibility owner

Handle `DismissRequested` inside the existing narrow owner:

```text
OverlayProcessController
```

Do not route it through `AddonProcessHost`, `SystemTrayIcon`, or a new UI manager.

Required behavior:

```text
DismissRequested arrives
→ schedule HandleDismissRequestedAsync
→ acquire existing _transition gate
→ if stopping, ignore
→ if no current ready server/process, ignore
→ if _visible == false, ignore
→ send existing OverlayCommand.Hide
→ require normal Hidden acknowledgement
→ only then set _visible = false
```

On failure:

```text
Hide not acknowledged / pipe failed
→ use the same existing session-retirement policy as a failed normal Toggle command
→ StopCurrentAsync
→ next explicit Show may relaunch
```

Do not invent a separate dismissal failure policy.

### Reuse existing serialization

The existing:

```text
_transition
```

already serializes real Show/Hide/process lifecycle work.

Use it.

Do not add:

- `_dismissGate`;
- command epochs;
- second semaphore;
- transition state machine;
- retry loop.

A small private helper to share the existing Hide send/ack/log/update path between tray Toggle and dismiss is acceptable if it reduces duplication.

Do not generalize it into a new command manager.

---

## 16. Stale-session handling — keep it simple

Process/pipe loss and fresh Overlay relaunch are real supported lifecycle paths.

If the server notification API naturally provides the source server instance, it is acceptable to verify that the dismissal came from the currently tracked `_server` before acting.

A simple reference-identity check is enough.

Do not add:

- session IDs;
- generations;
- epochs;
- reconnect tokens.

Also unsubscribe the current server dismissal notification during `StopCurrentAsync()` before disposing that server if the chosen event shape requires it.

This is ordinary cleanup, not a second ownership system.

---

## 17. Hook lifecycle relative to Show/Hide animation

Preferred Show order:

```text
Runtime Show
→ fresh ConfigureWindow()
→ prepare AnimatedContent initial state
→ ShowWithoutActivation()
→ install/arm outside-click watcher
→ run 180 ms Show animation
→ canonical visible visual state
→ Visible acknowledgement
```

The hook may be armed immediately after the HWND becomes visible so a click during the opening animation can still dismiss the surface.

Preferred Hide order:

```text
Runtime Hide
→ disarm/uninstall outside-click watcher immediately
→ run 150 ms Hide animation
→ WindowInterop.Hide()
→ reset hidden visual state
→ Hidden acknowledgement
```

This avoids generating additional dismiss requests while the panel is already closing.

If Show fails before the window becomes usable, do not leave a hook installed.

If animation fails but the existing fail-soft policy keeps the Overlay visible, the outside-click watcher should remain consistent with the actual visible window.

---

## 18. Hook installation failure is fail-soft

Outside-click dismissal is UX, not controller safety in this PR.

If `SetWindowsHookEx(WH_MOUSE_LL, ...)` fails:

```text
log warning/error with Win32 code
→ keep Overlay Show successful
→ panel remains usable through existing tray Toggle
```

Do not retire an otherwise healthy Overlay session solely because mouse outside-click dismissal could not be installed.

Do not retry in a timer loop.

The next Show may make one normal install attempt again after the prior Hide/reset cycle.

---

## 19. Required Win32 pieces

Use only the minimal APIs needed by this POC, expected to include equivalents of:

```text
SetWindowsHookExW
UnhookWindowsHookEx
CallNextHookEx
GetWindowRect
GetForegroundWindow    // existing
```

and the relevant structures/constants:

```text
WH_MOUSE_LL
MSLLHOOKSTRUCT
WM_LBUTTONDOWN
WM_RBUTTONDOWN
WM_MBUTTONDOWN
WM_XBUTTONDOWN
```

Do not add a native DLL.

Do not use DLL injection.

`WH_MOUSE_LL` executes through the installing process callback mechanism and is sufficient for this narrow user-session POC.

Keep the delegate strongly rooted for the hook lifetime.

Ensure teardown is idempotent.

---

## 20. Activation diagnostics improvement

Keep the existing low-rate message-subclass diagnostics.

For `WM_ACTIVATE`, add enough context to distinguish:

```text
Overlay received activation-related message
```

from:

```text
Overlay is actually the current foreground HWND
```

Recommended fields:

```text
State
OverlayHwnd
ForegroundHwnd
IsOverlayForeground
```

For `WM_MOUSEACTIVATE`, also include current foreground HWND if straightforward.

Do not add periodic foreground polling.

Do not add WinEvent foreground watchers solely for this diagnostic.

Do not automatically call `SetForegroundWindow()` to restore another process.

Do not suppress all `WM_ACTIVATE` messages.

The existing no-activate implementation remains:

```text
WS_EX_NOACTIVATE
SWP_NOACTIVATE
WM_MOUSEACTIVATE → MA_NOACTIVATE
```

until real supported-product evidence proves it insufficient.

---

## 21. Outside-click diagnostics

Add only low-rate event logs.

On the first outside button-down that requests dismissal, log conceptually:

```text
[Input] Outside click dismissal requested
OverlayHwnd=...
Message=WM_LBUTTONDOWN
PointerX=...
PointerY=...
WindowLeft=...
WindowTop=...
WindowRight=...
WindowBottom=...
ForegroundHwnd=...
```

Runtime should log conceptually:

```text
[Overlay] Overlay dismiss request received. PID=...
[Overlay] Overlay command requested. Command=Hide Reason=OutsideClick PID=...
[Overlay] Overlay command acknowledged. Command=Hide Reason=OutsideClick PID=... ElapsedMs=...
```

Exact wording may follow current `AppLog` style.

Do not log:

- mouse move;
- every inside click;
- wheel movement;
- hook callback frequency;
- per-frame animation data.

---

## 22. Architecture document update

Make a small corresponding update to:

```text
docs/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
```

Add the mouse transient-surface contract without rewriting the document.

The architecture should state conceptually:

```text
mouse/pointer outside-click
→ Overlay process detects local geometry hit-test
→ sends semantic dismissal request to Runtime
→ Runtime remains visibility/capture authority
→ Runtime performs normal Hide/close sequence
```

Also state:

```text
inside Overlay click
→ remains open
```

Do not claim controller `Back`/physical-button behavior is implemented by this PR.

Do not rewrite OQ3/OQ4 future sections.

---

## 23. Files expected to change

Primary expected files:

```text
src/SteamInputAddonforClaw.Overlay/
    OverlayWindow.xaml
    OverlayWindow.xaml.cs
    WindowInterop.cs
    App.xaml.cs

src/SteamInputAddonforClaw.FrontendTransport/
    OverlayWire.cs

src/SteamInputAddonforClaw/Lifecycle/
    OverlayProcessController.cs

tests/SteamInputAddonforClaw.Tests/
    OverlayTransportTests.cs

docs/
    ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
```

A tiny focused helper/test file is acceptable only if it is clearly simpler than inflating an existing file.

Do not modify unrelated controller ownership files.

Do not modify QamHost.

Do not modify Main UI lifecycle.

Do not modify TDP/Profile/Power/FPS feature authorities.

---

## 24. Required transport tests

Add focused automated tests for the new wire behavior.

### 24.1 Dismiss frame round-trip

Verify:

```text
client connected and Ready
→ client sends DismissRequested
→ server observes exactly one dismissal notification
```

`DismissRequested` must not mutate server `State` by itself.

If current state is Visible:

```text
DismissRequested
→ State still Visible
```

until Runtime sends Hide and the client reports Hidden.

### 24.2 Dismiss does not complete an existing command acknowledgement

Verify that `DismissRequested` is not interpreted as:

```text
Visible
Hidden
```

and cannot falsely satisfy Show/Hide acknowledgement.

### 24.3 Runtime-driven dismissal completes through Hide/Hidden

Test the real intended sequence at the `OverlayProcessController`/server seam where practical:

```text
Show
→ Visible
→ client sends DismissRequested
→ Runtime sends Hide
→ client handles Hide
→ Hidden
→ Runtime visible state becomes false
```

The test must prove there is no receive-loop deadlock.

A reasonable bounded completion time is enough.

Do not add artificial instruction-level race tests.

### 24.4 Duplicate dismiss requests do not create repeated Hide transitions

At minimum prove one of these according to final implementation shape:

- Overlay-side one-per-visible gate emits one request; or
- Runtime ignores dismissal once `_visible` is already false/closing.

Do not build a stress test requiring pathological click timing.

### 24.5 Existing transport tests remain green

Preserve:

```text
version mismatch rejection
oversized frame rejection
Ready without unsolicited Hidden
immediate Show after Ready
warm Show/Hide reuse
reconnect after process/session loss
failed command retires session
Shutdown
```

---

## 25. Hook logic testing

Do not build a Windows-wide integration-hook test framework merely for this PR.

Keep pure logic testable where straightforward.

For example, extract or expose only a tiny point-in-rect predicate if needed to test:

```text
point inside rect  → false dismiss
point on valid inside boundary → false dismiss
point left/right/top/bottom outside → true dismiss
```

Do not introduce an `IMouseHook`, fake hook framework, or dependency injection abstraction solely for unit tests.

Native hook installation itself is validated manually on the target MSI Claw.

---

## 26. Manual hardware validation — Windows desktop

Primary reference environment:

```text
MSI Claw
1920 × 1200
150%
DPI 144
```

### Test A — visual surface tone

```text
Show Overlay on Windows desktop
```

Confirm:

- panel is neutral dark gray rather than the previous near-black blue-gray;
- background is approximately `#2B2B2B`;
- only the left 400-DIP / 600px Overlay region is covered;
- no full-screen dark layer;
- no Acrylic/transparent artifact;
- existing text remains legible.

### Test B — inside clicks keep Overlay visible

Show Overlay, then click at least:

```text
header text area
geometry text area
blank panel padding/background
multiple different positions inside the 600px panel
```

Confirm:

```text
Overlay remains visible
no DismissRequested log
no Hide command
no process restart
```

### Test C — outside desktop click dismisses

Show Overlay, then left-click the Windows desktop to the right of the panel.

Expected:

```text
outside click detected once
→ DismissRequested
→ Runtime Hide
→ 150 ms animation
→ Hidden ACK
→ same Overlay.exe remains alive
```

Confirm the desktop click is still delivered normally.

### Test D — taskbar/outside target

Show Overlay and click the Windows taskbar outside the panel.

Expected:

```text
Overlay dismisses
+ taskbar receives the original click
```

### Test E — right click outside

Show Overlay and right-click the desktop outside the panel.

Expected:

```text
Overlay dismisses
+ underlying desktop context-click is not swallowed
```

The exact Explorer context-menu visual result can vary; the key requirement is that the hook does not consume the input.

### Test F — warm reuse

Repeat:

```text
Show
outside click dismiss
Show
outside click dismiss
```

at least 10 times.

Confirm:

```text
same Overlay PID
same HWND
same warm pipe session
no reconnect
no command timeout
no broken pipe
```

---

## 27. Manual no-activate diagnostic validation

This PR does not attempt to solve an unproven activation bug.

Use the improved logs to check reality.

### Desktop test

While Overlay is visible:

```text
click inside Overlay
```

Inspect any `WM_MOUSEACTIVATE` / `WM_ACTIVATE` entries.

If `WM_ACTIVATE State=1` appears, the log must now show:

```text
ForegroundHwnd
IsOverlayForeground
```

Do not infer foreground ownership from `State=1` alone.

### Optional supported-game check

If practical during this POC:

```text
launch a normal windowed/borderless game
→ show Overlay while game owns foreground
→ click inside Overlay
```

Confirm the Overlay does not intentionally become foreground.

If real hardware demonstrates that the Overlay becomes foreground despite the current no-activate contract, document it as a separate proven issue for the next focused PR.

Do not expand OQ-UX-A into focus-restoration architecture during implementation.

---

## 28. Process/transport failure behavior remains unchanged

Outside-click support must not weaken the already-proven warm lifecycle.

### Overlay hidden and process dies

```text
Runtime/controller unaffected
→ next explicit Show may make one fresh launch attempt
```

### Overlay visible and process dies

OQ-UX-A still has no controller capture.

Therefore:

```text
controller remains live
Runtime retires Overlay session
next explicit Show may relaunch
```

### Dismiss Hide acknowledgement fails

```text
Runtime retires current Overlay session
→ feature-local failure
```

Do not retry Hide repeatedly.

Do not add watchdogs.

---

## 29. Strict non-goals

Do not implement in OQ-UX-A:

- controller capture;
- virtual-output neutralization;
- OverlayInputRouter;
- physical WING/OEM1 mapping;
- controller Back/Accept navigation;
- Steam QAM visible-surface arbitration;
- Main UI visible-surface arbitration;
- Game Bar suppression changes;
- TDP controls;
- CPU Boost controls;
- FPS controls;
- Power Mode controls;
- profile controls;
- telemetry polling;
- raw 250 Hz controller IPC;
- touch-specific global pointer framework;
- low-level keyboard hook;
- mouse remapping;
- click-through Overlay;
- full-screen invisible input window;
- hidden owner-window hierarchy;
- Acrylic/Mica/blur/transparency;
- DirectComposition host redesign;
- Direct2D renderer;
- per-frame HWND animation;
- polling `GetCursorPos` timer;
- foreground polling loop;
- generalized `OverlaySurfaceManager`;
- generalized input/event bus;
- heartbeat;
- watchdog;
- request IDs;
- epochs;
- barriers;
- transport retry framework.

Keep the PR small and directly tied to the demonstrated UX requirements.

---

## 30. Full PID1902 invariants remain untouched

This PR must not change:

```text
Center M authority
PID1901 / PID1902
DirectInput ownership
HidHide baseline
VIIPER server/bus
Xbox360 presentation
SteamDeck presentation
Steam/BPM detection
presentation switching
owned DirectInput recovery
suspend/resume behavior
PnP handling
```

Overlay remains a disposable frontend.

The one-owner rule remains:

```text
Overlay detects local UI intent
Runtime owns visibility/capture state
```

No Overlay failure may become controller-authority failure.

---

## 31. Expected logging after implementation

Healthy Show:

```text
Runtime:
Overlay command requested. Command=Show PID=...
Overlay command acknowledged. Command=Show PID=... ElapsedMs≈200

Overlay:
Show received
Geometry applied
Show animation started ... ContentSlideDistanceDip=32
Show animation completed
Show completed
Outside-click watcher armed
```

Healthy inside click:

```text
no dismissal log
no Hide command
```

Healthy outside click:

```text
Overlay:
Outside click dismissal requested ...

Runtime:
Overlay dismiss request received. PID=...
Overlay command requested. Command=Hide Reason=OutsideClick PID=...

Overlay:
Hide received
Hide animation started
Hide animation completed
Hide completed

Runtime:
Overlay command acknowledged. Command=Hide Reason=OutsideClick PID=...
```

Healthy Hidden state:

```text
outside-click hook not installed
warm Overlay process remains alive
```

No normal path should produce:

```text
not acknowledged ElapsedMs=0/1
Pipe is broken
forced process termination
hook installed while hidden
repeated DismissRequested spam
```

---

## 32. Verification commands

Run normal repository verification:

```powershell
dotnet restore SteamInputAddonforClaw.slnx

dotnet build SteamInputAddonforClaw.slnx `
    -c Release `
    --no-restore

dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj `
    -c Release `
    --no-build `
    --no-restore

git diff --check
```

Also run the repository's current publish/layout verification required by CI, including the existing Overlay staged payload verification.

No packaging topology change is expected.

---

## 33. PR size and implementation style

Keep this as one focused PR and preferably comfortably below 500 changed production LOC.

Expected code is mostly:

```text
one XAML color change
+
small WindowInterop mouse-hook lifetime
+
one DismissRequested wire message
+
small Runtime dismiss handler using existing _transition/Hide path
+
focused tests/log additions
+
small architecture-doc clarification
```

Prefer direct code over new abstractions.

Do not refactor the full Overlay transport.

Do not refactor `OverlayProcessController` into a generic child-process framework.

Do not refactor unrelated Win32 interop.

Do not perform unrelated UI polish.

---

## 34. Acceptance criteria

The PR is complete only when all of the following are true.

### Surface

- [ ] `OpaquePanel` background changed from `#FF20242B` to `#FF2B2B2B`.
- [ ] Panel remains fully opaque.
- [ ] No Acrylic/Mica/blur/transparency added.
- [ ] Existing 32 DIP content animation preserved.
- [ ] Existing 180 ms Show / 150 ms Hide preserved.
- [ ] Existing WorkArea/DPI/400-DIP geometry preserved.

### Outside click

- [ ] Overlay internal click does not dismiss.
- [ ] Blank internal panel-area click does not dismiss.
- [ ] Mouse button-down outside current HWND requests dismissal.
- [ ] Left/right/middle/X button-down are handled as defined.
- [ ] Mouse move/hover/wheel alone do not dismiss.
- [ ] Outside click is not swallowed.
- [ ] No `WS_EX_TRANSPARENT` used.
- [ ] No full-screen catcher window added.
- [ ] `WH_MOUSE_LL` is installed only while visible.
- [ ] Hook is removed on Hide and teardown.
- [ ] Hook callback does not perform synchronous pipe I/O.
- [ ] Only one dismissal request is emitted per visible interval.

### Visibility authority

- [ ] Overlay never locally hides itself solely because outside click was detected.
- [ ] Overlay sends semantic `DismissRequested` to Runtime.
- [ ] Runtime sends the existing `Hide` command.
- [ ] Runtime waits for `Hidden` acknowledgement.
- [ ] Runtime changes `_visible` only through the Runtime-owned transition path.
- [ ] Failed dismiss Hide uses existing session-retirement policy.
- [ ] Existing `_transition` remains the only lifecycle/visibility serialization gate.
- [ ] No new visibility manager/state machine added.

### Transport

- [ ] `OverlayWireMessageKind.DismissRequested` added.
- [ ] Overlay protocol version bumped from 1 to 2.
- [ ] Desktop/QAM frontend protocol versions unchanged.
- [ ] DismissRequested itself does not mutate `OverlayState`.
- [ ] DismissRequested does not satisfy Show/Hide acknowledgement.
- [ ] Server receive loop remains free to receive the later Hidden ACK.
- [ ] No receive-loop deadlock.
- [ ] No request IDs/epochs/generations added.

### Diagnostics

- [ ] `WM_ACTIVATE` logs current foreground HWND.
- [ ] `WM_ACTIVATE` logs whether Overlay is actually foreground.
- [ ] Outside dismissal logs one low-rate event with pointer/window geometry.
- [ ] No mouse-move spam.
- [ ] No foreground polling loop.

### Regression

- [ ] Warm Overlay startup still succeeds.
- [ ] Repeated Show/Hide still reuses one process/window/session.
- [ ] Ready remains canonical hidden startup state.
- [ ] No unsolicited startup Hidden restored.
- [ ] Fresh Show does not regress to 0–1 ms false acknowledgement.
- [ ] Normal Show/Hide produces no broken pipe.
- [ ] Graceful Runtime shutdown still exits Overlay cleanly.
- [ ] Full test suite passes.
- [ ] Release build passes.
- [ ] `git diff --check` clean.

### Manual MSI Claw validation

- [ ] 1920×1200 @150% panel is neutral gray `#2B2B2B`.
- [ ] Only Overlay region is covered.
- [ ] At least 10 inside clicks keep Overlay visible.
- [ ] At least 10 outside-click dismissal cycles succeed.
- [ ] Same PID/HWND reused throughout normal cycles.
- [ ] Underlying desktop click remains functional.
- [ ] Right-click outside is not swallowed.
- [ ] No outside-click hook remains active while hidden.
- [ ] Activation logs are sufficient to identify actual foreground ownership.

---

## 35. Final implementation principle

The required product behavior is simple:

```text
Overlay is a transient surface.
```

But because it is intentionally `NOACTIVATE`, do not force it into a normal focus-owned popup model.

Use the existing ownership split:

```text
Overlay process
→ knows its HWND geometry
→ detects an outside mouse button-down
→ emits one semantic dismissal request

Runtime
→ owns visible/hidden state
→ performs the same bounded Hide command
→ receives Hidden acknowledgement
```

And keep the visual correction equally simple:

```text
#20242B
→ #2B2B2B
```

No extra UI authority, no focus proxy, no polling loop, no generalized input manager, and no controller lifecycle changes are needed for this PR.
