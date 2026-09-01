# Work Order — OQ-ANIM-A: Overlay Show/Hide Animation POC

## Status

Implementation work order for a focused visual-animation proof of concept on top of the merged warm Overlay lifecycle and diagnostic logging work.

Use the label:

```text
OQ-ANIM-A
```

Do not number this as part of the Full PID1902 controller-authority PR sequence.

Current `main` baseline when this work order was prepared:

```text
ac0a0bad359cd183388491953a1a64b4af269675
Add Overlay POC diagnostic logging (#438)
```

Merged prerequisites:

```text
OQ-POC-A
→ native WinUI3 Overlay window viability

OQ-POC-B / PR #436
→ Runtime-owned warm hidden Overlay.exe
→ dedicated .Overlay transport
→ Show / Hide / Shutdown
→ Ready / Visible / Hidden acknowledgement
→ temporary tray "Overlay POC: Toggle"

OQ-LOG-A / PR #438
→ overlay-<PID>.log
→ Runtime PID + startup/command timing
→ geometry / WorkArea / DPI diagnostics
→ persistent Overlay-side failure logging
```

Read before implementation:

- `docs/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/OQ_POC_A_OVERLAY_WINDOW_VIABILITY_WORK_ORDER.md`
- `docs/work-order/OQ_POC_B_OVERLAY_TRANSPORT_WARM_LIFECYCLE_WORK_ORDER.md`
- `docs/work-order/OQ_LOG_A_OVERLAY_POC_DIAGNOSTIC_LOGGING_WORK_ORDER.md`
- current `main` implementations of:
  - `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml`
  - `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
  - `src/SteamInputAddonforClaw.Overlay/WindowInterop.cs`
  - `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
  - `src/SteamInputAddonforClaw.Overlay/Diagnostics/OverlayLog.cs`
  - `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`

This PR is a visual POC only.

It must not change controller ownership, controller capture, PID/HidHide/VIIPER behavior, Steam/BPM presentation policy, QamHost behavior, physical-button mapping, or frontend transport protocol.

---

## 1. Goal

Add one minimal, compositor-friendly opening/closing effect to the existing Addon Overlay so the POC feels closer to a handheld Quick Settings surface without adding meaningful steady-state CPU/GPU load.

Freeze the initial visual target to:

```text
Show
- slide from left
- duration: 180 ms
- opacity: 0.90 → 1.00
- easing: ease-out

Hide
- slide to left
- duration: 150 ms
- opacity: 1.00 → 0.90
- easing: ease-in
```

No other effects are part of this PR.

The POC should answer:

> Can the already-warm WinUI3 Overlay use a short composition animation on Show/Hide while preserving no-activate window behavior, low idle cost, current geometry, and reliable Runtime acknowledgements?

---

## 2. Non-goals

Strictly out of scope:

- Acrylic;
- Mica;
- blur;
- background capture;
- scale animation;
- spring/bounce animation;
- drop-shadow animation;
- glow animation;
- gradient animation;
- continuously animated controls;
- frame-rate timers;
- `DispatcherTimer` animation loops;
- per-frame `SetWindowPos` loops;
- Direct2D renderer work;
- DirectComposition host redesign;
- transparent/layered-window architecture work;
- controller capture / neutralization;
- WING / OEM1 mapping;
- Steam QAM visible-surface arbitration;
- Game Bar suppression changes;
- new Overlay protocol messages;
- animation state persistence;
- generalized animation manager/service.

The goal is a small reversible visual experiment.

---

## 3. Preserve the current window authority split

Current ownership remains:

```text
WindowInterop
→ HWND geometry / monitor / WorkArea / topmost / no-activate / show / hide

OverlayWindow
→ XAML presentation and this POC animation

Runtime
→ process lifecycle and .Overlay command ownership
```

Do not move geometry authority into XAML animation code.

Do not make Runtime aware of animation properties.

Do not make `WindowInterop` into an animation engine.

The existing no-activate contract remains authoritative.

---

## 4. Preferred animation technology

Use WinUI / Microsoft.UI.Composition render-time animation on the Overlay XAML surface.

Preferred direction:

```text
Microsoft.UI.Xaml.Hosting.ElementCompositionPreview
+
UIElement render-time Translation
+
Composition opacity animation
```

or the equivalent direct WinUI3 Composition API available in the repository's current Windows App SDK version.

The important contract is:

```text
layout stays fixed
HWND stays at final geometry
animation runs in the XAML/composition visual layer
```

Do not animate layout properties such as:

- `Margin`;
- `Width`;
- `Canvas.Left` through a UI-thread loop;
- repeated `AppWindow.Move`;
- repeated `SetWindowPos`.

Do not add a timer that calculates 60/120 animation frames manually.

Microsoft's WinUI/Composition interop already supports render-time translation and opacity; use that existing mechanism rather than inventing a frame scheduler.

---

## 5. XAML shape

The current root is one opaque `Grid`.

Refactor only as much as needed to distinguish:

```text
Window/XAML host viewport
└─ animated opaque panel surface
```

Conceptually:

```xml
<Grid x:Name="AnimationViewport">
    <Grid x:Name="AnimatedPanel"
          Background="#FF20242B"
          Padding="28">
        ... existing POC content ...
    </Grid>
</Grid>
```

Preserve existing content and geometry unless a tiny naming/layout change is required to animate the panel.

Do not redesign the POC visual appearance in this PR.

Do not add cards, icons, production Quick Settings controls, or navigation styling.

### Important backing-surface constraint

This POC should first attempt to animate the actual opaque panel surface left/right inside the already-positioned WinUI window.

However, do **not** broaden the PR into unsupported transparent-window hacks if WinUI's top-level backing surface causes a visible blank/theme-colored rectangle while the panel is translated.

Explicitly do not add in this PR solely to hide such an artifact:

- `WS_EX_LAYERED` transparency machinery;
- color-key transparency;
- custom DirectComposition host;
- WPF;
- WinUIEx dependency;
- custom native compositor process;
- game capture/background mirroring.

If hardware testing proves the backing surface prevents a visually clean full-panel slide, document that real result and handle it in a later focused design decision rather than expanding this POC.

---

## 6. Show sequence

Keep the current fresh geometry behavior.

Desired Show path:

```text
Runtime sends Show
→ Overlay dispatcher receives Show
→ OverlayWindow recomputes current monitor / WorkArea / DPI
→ HWND is placed at final geometry using existing WindowInterop.Configure
→ AnimatedPanel is prepared in initial visual state
      Translation.X = negative slide distance
      Opacity = 0.90
→ HWND becomes visible using existing no-activate show path
→ composition animation starts immediately
      Translation.X → 0
      Opacity → 1.00
      Duration = 180 ms
      Ease-out
→ animation completes
→ canonical final visual state is committed
      Translation = 0
      Opacity = 1.00
→ Show command handler completes
→ existing transport sends Visible
```

### 6.1 Avoid first-frame flash

The panel must not visibly appear for one frame at its final position before jumping to the animation start position.

Set the initial render-time translation/opacity before calling the final `ShowWithoutActivation()` path.

Do not start the timed animation while the HWND is still hidden if doing so allows most/all of the 180 ms duration to elapse before the window is visible.

Correct intent:

```text
prepare initial visual state while hidden
→ show HWND no-activate
→ start animation immediately
```

---

## 7. Show slide distance

The slide should represent the panel entering from the left rather than a small decorative text nudge.

Preferred distance:

```text
final panel width expressed in XAML/DIP coordinates
```

Do not hard-code a physical `600 px` value.

The 1920×1200 @150% reference remains:

```text
POC panel width = 400 DIP
physical width ≈ 600 px at 144 DPI
```

but the animation must remain DPI-independent.

The existing geometry path already knows final physical width and DPI. If the animation needs a DIP slide distance, derive it from the final geometry rather than adding scale-specific cases.

Conceptually:

```text
slideDistanceDip = finalPhysicalWidth * 96 / dpi
```

or use the actual XAML panel width when it is already authoritative and non-zero.

Do not add 100/125/150/175/200% hard-coded branches.

---

## 8. Hide sequence

Desired Hide path:

```text
Runtime sends Hide
→ Overlay dispatcher receives Hide
→ panel is currently canonical visible state
      Translation = 0
      Opacity = 1.00
→ composition animation starts
      Translation.X → negative slide distance
      Opacity → 0.90
      Duration = 150 ms
      Ease-in
→ animation completes
→ HWND is hidden using existing WindowInterop.Hide
→ visual properties reset while hidden to a known canonical baseline
→ Hide command handler completes
→ existing transport sends Hidden
```

Do not hide the HWND before the 150 ms animation finishes.

Do not close/recreate the window.

Do not disconnect/reconnect the `.Overlay` pipe.

The same warm process/window/session must remain alive.

---

## 9. Acknowledgement semantics for this POC

Do not change `OverlayTransportProtocol`.

Keep the existing protocol messages:

```text
Show
Hide
Shutdown

Ready
Visible
Hidden
```

For OQ-ANIM-A, define command completion as:

```text
Show handler completion
= show animation completed and panel reached canonical visible state

Hide handler completion
= hide animation completed, HWND hidden, and panel reset to canonical hidden baseline
```

Therefore the existing `NamedPipeOverlayClient` naturally sends:

```text
Visible after ~180 ms animation completion
Hidden after ~150 ms animation completion
```

This is intentional for this POC because PR #438 already measures Runtime command → acknowledgement latency.

Expected healthy Runtime measurements should therefore include roughly:

```text
Show ACK latency >= ~180 ms + small command/dispatch overhead
Hide ACK latency >= ~150 ms + small command/dispatch overhead
```

Do not add separate `AnimationStarted` / `AnimationFinished` IPC messages.

A later controller-capture PR may decide a different user-input/capture commit boundary; do not solve that future contract here.

---

## 10. Overlay-side animation logging

Reuse the merged `OverlayLog` from OQ-LOG-A.

Add only low-rate animation milestones useful for the hardware POC.

Recommended records:

```text
[Animation] Show animation started
DurationMs=180
StartOpacity=0.90
EndOpacity=1.00
SlideDistanceDip=...

[Animation] Show animation completed
ElapsedMs=...

[Animation] Hide animation started
DurationMs=150
StartOpacity=1.00
EndOpacity=0.90
SlideDistanceDip=...

[Animation] Hide animation completed
ElapsedMs=...
```

If animation creation/start/completion fails, persist the original exception and command context.

Do not log animation frames.

Do not log current Translation/Opacity every tick.

Do not add a metrics framework.

The Runtime's existing Show/Hide PID/ElapsedMs logs remain unchanged and provide the outer measurement.

---

## 11. Failure policy

Animation is presentation only.

It must never become a reason to strand the Overlay or Runtime.

### Show animation failure

If initial geometry/show succeeds but the visual animation fails:

Preferred fail-soft behavior:

```text
log the animation failure
→ force panel into canonical visible state
      Translation = 0
      Opacity = 1
→ keep HWND visible
→ complete Show normally if the window is otherwise usable
```

Do not retire the Overlay process solely because an optional visual effect failed.

A real `WindowInterop.ShowWithoutActivation()` failure remains a real Show failure and should continue through the existing command failure path.

### Hide animation failure

If the optional visual animation fails:

```text
log the animation failure
→ hide HWND immediately through existing WindowInterop.Hide
→ reset visual state
→ complete Hide if the HWND hide succeeds
```

A real `WindowInterop.Hide()` failure remains an actual Hide failure.

The visual effect must not weaken current lifecycle reliability.

---

## 12. Command serialization / race policy

Do not add an animation state machine.

Current Runtime POC toggles already serialize through the existing Overlay process/controller command path.

The normal sequence is human-scale:

```text
Show
→ wait Visible acknowledgement
→ next Toggle
→ Hide
→ wait Hidden acknowledgement
```

That is sufficient for this POC.

Do not introduce:

- animation epochs;
- cancellation generations;
- animation ownership manager;
- pending transition queue;
- barrier;
- new lock hierarchy.

Do not defend against theoretical instruction-level overlap that current Runtime command serialization does not expose in normal product use.

Real process shutdown during an animation only needs to remain bounded by the existing Runtime lifecycle; 150–180 ms is short relative to the existing command/shutdown bounds.

---

## 13. Shutdown behavior

`Shutdown` is not a visual Hide request.

Do not require the 150 ms Hide animation before controlled Runtime shutdown.

Current shutdown contract should remain direct and bounded:

```text
Runtime sends Shutdown
→ Overlay closes/disposes
→ process exits
```

Aesthetic closing animation is not worth delaying controller/runtime teardown.

Do not modify the existing 3-second process shutdown bound for this PR.

---

## 14. No-activate / foreground preservation

Animation must not alter the existing foreground ownership contract.

Preserve:

```text
WS_EX_NOACTIVATE
SWP_NOACTIVATE
Topmost Overlay HWND
no intentional activation/focus transfer
```

The composition animation must not call `Activate()` merely to make animation APIs work.

Manual validation must confirm:

```text
game/window foreground before Show
→ Overlay animates in
→ game/window remains foreground
→ Overlay animates out
→ foreground remains unchanged
```

Do not add synthetic foreground-restoration code unless real testing demonstrates the existing no-activate contract regressed.

---

## 15. Idle performance contract

This PR must not change hidden idle behavior.

After the Overlay is hidden and animation completes:

```text
no active animation
no timer
no render loop created by this feature
no DPI polling
no monitor polling
no telemetry polling
no controller polling
```

When visible but static:

```text
no continuous animation from OQ-ANIM-A
```

GPU/CPU work from this feature should exist only during the approximately 180 ms Show and 150 ms Hide intervals.

Do not create a permanent composition animation clock or repeating keyframe animation.

---

## 16. Reduced/system animation preference

If the current Windows/WinUI API provides a direct, low-complexity way to honor the user's system animation preference, respect it.

Preferred simple behavior:

```text
AnimationsEnabled == true
→ run 180/150 ms effect

AnimationsEnabled == false
→ skip effect
→ Show/Hide immediately using current window behavior
```

Do not add a watcher/service for this setting.

A one-shot check when performing Show/Hide is enough.

If honoring the setting requires new infrastructure or a brittle interop layer, document and defer rather than expanding this POC.

---

## 17. Files expected to change

Likely focused diff:

```text
src/SteamInputAddonforClaw.Overlay/
    OverlayWindow.xaml
    OverlayWindow.xaml.cs
    Diagnostics/OverlayLog.cs       # only if tiny logging helper additions are needed
```

Potentially:

```text
App.xaml.cs
```

only if the current command handler needs to await asynchronous Show/Hide methods.

`WindowInterop.cs` should normally remain functionally unchanged except for a tiny helper if unavoidable.

Do not modify Runtime lifecycle or transport code unless compilation proves one very small signature adjustment is required.

Do not modify:

```text
OverlayWire protocol/version
NamedPipeOverlayServer
NamedPipeOverlayClient semantics
controller runtime
HidHide
VIIPER
QamHost
Game Bar
WING/OEM1
publish layout
installer
```

unless a direct build regression proves a tiny mechanical change is necessary.

---

## 18. Suggested implementation shape

A small `OverlayWindow` implementation is preferred over introducing a new manager.

Conceptually:

```csharp
private static readonly TimeSpan ShowDuration = TimeSpan.FromMilliseconds(180);
private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(150);

internal async Task ShowForPocAsync()
{
    ConfigureWindow(...);
    PrepareShowVisualState(...);
    WindowInterop.ShowWithoutActivation(this);
    await PlayShowAnimationAsync(...);
    CommitVisibleVisualState();
}

internal async Task HideForPocAsync()
{
    await PlayHideAnimationAsync(...);
    WindowInterop.Hide(this);
    ResetHiddenVisualState();
}
```

The exact helper names are not mandated.

Keep helpers private to `OverlayWindow` where possible.

Do not introduce an `IOverlayAnimator`, `OverlayAnimationManager`, `AnimationCoordinator`, or service registration solely for this effect.

### Composition completion

Use a native Composition/XAML completion mechanism appropriate to the selected API so the returned `Task` completes when the compositor animation ends.

A scoped composition batch or equivalent single completion signal is acceptable.

Do not implement completion with:

```text
Task.Delay(180)
```

while separately launching an untracked animation unless the chosen WinUI API genuinely has no completion signal.

The command acknowledgement should represent the actual animation completion path, not merely the passage of an assumed duration.

---

## 19. Easing

Keep easing simple.

### Show

```text
Ease-out
```

Use one ordinary cubic-bezier / compositor easing curve with no overshoot.

### Hide

```text
Ease-in
```

Use one ordinary cubic-bezier / compositor easing curve with no overshoot.

Do not spend this PR tuning a complex motion system.

Do not add spring physics.

The exact curve may use the nearest standard WinUI/Composition ease-in/ease-out curve available.

Hardware feel matters more than matching an arbitrary mathematical curve exactly.

---

## 20. Tests

Do not create fake architecture solely to unit-test Composition APIs.

Required automated coverage should focus on deterministic logic only.

If duration/opacity/slide-distance calculation is extracted as ordinary values/helpers, cover at least:

```text
ShowDuration = 180 ms
HideDuration = 150 ms
Show opacity 0.90 → 1.00
Hide opacity 1.00 → 0.90
DPI-independent slide-distance calculation when a helper exists
```

If these values remain private constants directly beside the implementation, a dedicated unit-test abstraction is not required.

More important automated regression checks:

- solution builds in Release;
- existing Overlay transport tests still pass;
- existing Overlay log tests still pass;
- full test suite passes;
- publish layout remains valid;
- no transport protocol version change.

Do not add screenshot-test infrastructure in this PR.

---

## 21. Manual hardware validation

Primary target:

```text
MSI Claw
1920 × 1200
150% scaling / 144 DPI
```

Use the temporary tray command:

```text
Overlay POC: Toggle
```

### 21.1 Warm Show

Validate:

- Overlay process is already warm/hidden before toggle;
- Show begins immediately after command;
- panel visually enters from the left;
- opacity transitions subtly from ~0.90 to 1.00;
- motion does not bounce/overshoot;
- taskbar remains uncovered;
- final width remains the expected 400 DIP / ~600 physical px at 150%;
- game/window remains foreground;
- same Overlay PID remains alive.

### 21.2 Warm Hide

Validate:

- panel slides left;
- opacity transitions from 1.00 to ~0.90;
- duration feels shorter than Show;
- HWND is actually hidden after animation;
- same process remains warm;
- no leftover blank rectangle remains onscreen;
- game/window remains foreground.

### 21.3 Repeated toggle

Run at least:

```text
Show
Hide
Show
Hide
Show
Hide
```

Confirm:

- same PID;
- no cumulative translation drift;
- no opacity drift;
- every Show ends at Translation=0 / Opacity=1;
- every Hide fully disappears;
- no animation remains active while hidden.

### 21.4 Logs

Collect Runtime log + `overlay-<PID>.log`.

Expected healthy pattern:

```text
Runtime: Overlay command requested Command=Show PID=...
Overlay: Show received
Overlay: Show animation started DurationMs=180 ...
Overlay: Show animation completed ElapsedMs≈180
Overlay: Show completed
Runtime: Overlay command acknowledged Command=Show PID=... ElapsedMs≈180+
```

Hide should show the analogous ~150 ms path.

Large unexplained latency beyond the animation duration should be investigated separately from the visual effect.

### 21.5 Performance

During testing observe:

- hidden idle CPU before animation;
- visible static CPU after Show completes;
- GPU activity during Show/Hide;
- any obvious game frametime hitch during toggle.

The goal is not mathematically zero cost during the 150–180 ms transition.

The acceptance criterion is:

> no persistent performance cost after animation completes and no obvious user-visible game hitch attributable to this small effect.

If desired, a later hardware-diagnostics session may use PresentMon, but do not add PresentMon integration to this PR.

### 21.6 Backing-surface artifact check

Specifically look for:

- black rectangle behind the moving panel;
- white/theme-colored rectangle behind the moving panel;
- static opaque window area exposed while the XAML panel translates;
- flicker at first/last frame.

If this occurs, treat it as a real WinUI-window rendering limitation to analyze next.

Do not patch around it in this PR with layered-window/renderer architecture.

---

## 22. CI / verification

At minimum run:

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

Also run the repository's normal publish-layout verification if CI normally does so.

Do not report the PR complete with failing existing Overlay transport/log tests.

---

## 23. Review checklist

The PR is ready only when all are true:

- [ ] Show uses 180 ms slide-left-to-final + opacity 0.90→1.00.
- [ ] Hide uses 150 ms slide-to-left + opacity 1.00→0.90.
- [ ] Show easing is simple ease-out.
- [ ] Hide easing is simple ease-in.
- [ ] No per-frame HWND movement loop exists.
- [ ] No `DispatcherTimer` animation loop exists.
- [ ] Existing `WindowInterop` geometry/no-activate contract remains intact.
- [ ] Show recomputes fresh monitor/WorkArea/DPI before animation.
- [ ] Visible ACK occurs after Show animation completion.
- [ ] Hidden ACK occurs after Hide animation and actual HWND hide completion.
- [ ] Animation failure fails soft without unnecessarily retiring Overlay.
- [ ] Real Show/Hide Win32 failures still fail through existing lifecycle handling.
- [ ] Shutdown remains direct/bounded and does not require aesthetic animation.
- [ ] Hidden/visible-static state has no continuing animation/timer.
- [ ] Existing Overlay diagnostics remain intact.
- [ ] No controller capture/input/presentation changes exist.
- [ ] No Overlay transport protocol/version change exists.
- [ ] No new animation manager/service/abstraction exists without a proven need.
- [ ] Release build and full tests pass.
- [ ] Hardware validation checks focus preservation and backing-surface artifacts.

---

## 24. Final implementation intent

The desired result is intentionally small:

```text
Warm hidden Overlay.exe
        ↓
Show command
        ↓
fresh WorkArea/DPI placement
        ↓
HWND shown no-activate at final geometry
        ↓
180 ms compositor slide-in + 0.90→1.00 opacity
        ↓
Visible ACK
        ↓
static / idle

Hide command
        ↓
150 ms compositor slide-out + 1.00→0.90 opacity
        ↓
HWND hidden
        ↓
Hidden ACK
        ↓
warm / idle
```

No blur.
No scale.
No continuous animation.
No controller changes.
No new lifecycle authority.
No per-frame HWND movement.

The purpose of OQ-ANIM-A is to determine whether this minimal motion treatment improves handheld feel while keeping the existing Overlay architecture simple and cheap.