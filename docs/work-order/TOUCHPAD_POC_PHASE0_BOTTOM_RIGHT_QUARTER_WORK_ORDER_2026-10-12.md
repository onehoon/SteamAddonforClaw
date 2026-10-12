# Work Order — TouchpadPoc Phase 0: Bottom-Right Quarter Invisible Touch Capture

**Date:** 2026-10-12  
**Repository:** `onehoon/SteamAddonforClaw`  
**Baseline:** Current `main`, including `docs/STEAM_TOUCHSCREEN_VIRTUAL_TRACKPAD_ARCHITECTURE_2026-10-12.md`  
**Implementer:** Local Codex  
**Deliverable:** One focused PR with one standalone diagnostic tool under `tools/TouchpadPoc/`.  
**Physical validation:** User after merge only; **not** a Codex deliverable or PR/CI/merge blocker.

## 1. Objective — only one rectangular zone

Implement a standalone **Windows 11 x64 .NET 10 C# console executable** to answer the Phase 0 feasibility question:

> Can a logically visible yet pixel-invisible Win32 topmost window receive physical touchscreen DOWN/MOVE/UP in exactly the **bottom-right one quarter of the primary screen**, without stealing focus, forwarding touch to the underlying desktop/game, or changing its VRR presentation behavior?

**This tool records evidence; it must not claim to prove touch isolation or VRR purely from receiving WM_POINTER.**

The initial experiment must use **exactly ONE** input region/window — **not two** and **not a full-screen invisible overlay**. This one-region diagnostic is an intentional simplification of the eventual left/right feature described in the architecture.

### Rectangle definition

- Use the **primary monitor's physical pixel bounds**, after declaring per-monitor-v2 DPI awareness *before* querying geometry or creating windows.
- Region is the bottom-right quadrant, i.e. the right 50% of the width and the bottom 50% of the height (**25% of total screen area**).
- Let primary bounds be `(L,T,W,H)`. Compute `X=L+floor(W/2)`, `Y=T+floor(H/2)`, `Width=W-floor(W/2)`, `Height=H-floor(H/2)`; handle odd dimensions without gaps.
- On the user's usual **1920 x 1200** physical display at **150% scaling**: `X=960, Y=600, Width=960, Height=600`. Do **not** mistakenly use 1280 x 800 DPI-scaled logical coordinates.
- Do not implement drag/resize, per-game placement, secondary-display selection, or another zone yet.
- Record primary monitor physical bounds, DPI-awareness result, screen DPI if available, window rectangle and HWND style in the startup log.

## 2. Scope / isolation

Proposed minimal structure:

```text
tools/TouchpadPoc/
  TouchpadPoc.csproj
  Program.cs
  README.md
  [only narrowly necessary extra .cs files]
```

Follow `tools/RumbleProbe/RumbleProbe.csproj` conventions where useful:
- `net10.0-windows10.0.26100.0`, x64 Windows target, framework-dependent console program.
- Can be built and run independently with `dotnet build tools/TouchpadPoc/TouchpadPoc.csproj -c Release` and `dotnet run --project tools/TouchpadPoc/TouchpadPoc.csproj -c Release`.
- **Do not** reference or modify the production Runtime, VIIPER, DirectInput, HidHide, MSI firmware, Steam, profiles, settings, Main App, Overlay, packaging, Velopack, startup tasks, system drivers, or native controller attachments.
- No admin elevation request, no persistent OS configuration change, no extra installed service, no global pointer redirection or `RegisterPointerInputTarget`.
- Do not add to `SteamInputAddonforClaw.slnx` or normal release bundles without a demonstrated necessity.
- Main App being WinUI3 is irrelevant: this temporary standalone console directly uses **P/Invoke Win32**, no separate C++ executable or WinUI3 window.

## 3. Candidate HWND and pointer input handling

Create a plain `WS_POPUP` top-level Win32 HWND covering **only the quadrant**.

First candidate extended styles:

```csharp
WS_EX_NOACTIVATE |
WS_EX_TOOLWINDOW |
WS_EX_NOREDIRECTIONBITMAP |
WS_EX_TOPMOST
```

- Apply `SetWindowPos(HWND_TOPMOST, X, Y, Width, Height, SWP_NOACTIVATE | ...)`, and show without activation. Verify by `GetWindowLongPtr(GWL_EXSTYLE)` and `GetWindowRect`, logging actual observed values.
- No `WS_EX_TRANSPARENT`, no `WM_NCHITTEST -> HTTRANSPARENT`, and no `WS_EX_LAYERED` in the first candidate. Return normal `HTCLIENT` for client hit test.
- `WM_MOUSEACTIVATE -> MA_NOACTIVATE`; `WM_POINTERACTIVATE -> PA_NOACTIVATE` as appropriate, following the **documented** return-value contract.
- Handle `WM_POINTERDOWN`, `WM_POINTERUPDATE`, `WM_POINTERUP`, and `WM_POINTERCAPTURECHANGED`; use `GetPointerType`/`GetPointerInfo` (or `GetPointerTouchInfo`) to verify **PT_TOUCH**, pointer ID, flags, coordinates, primary pointer, contact state.
- Prefer consistent handled-message behavior for the same touch pointer sequence; do not selectively call `DefWindowProc` in ways that cause mouse promotion to a game. Non-touch/ordinary default window messages can follow standard `DefWindowProc` where appropriate.
- Pointer down implicitly captures the pointer to the receiving window. Track IDs from Down until Up/cancel/capture loss. A missing UP after `WM_POINTERCAPTURECHANGED` must not leave a permanently active touch in diagnostic state.
- If a pointer's first DOWN occurs outside the quadrant, do not acquire it later just because it crosses into the region.
- No independent Raw Input monitor, mouse/keyboard injection, UIAccess manifest, `SetWindowFeedbackSetting` unless essential and explicitly recorded, PnP touchscreen disable, synthetic controller output, mouse hooks, or device handling.
- Do not draw any UI or diagnostics into the capture window: **no painting, D3D11, Direct2D, DirectComposition, WinUI3, swapchain, Present loop, timer-driven redraw, visible border, alpha fill, icon, text, or cursor indicator**. The window must not intentionally submit a graphic surface. Even without submitted pixels, this HWND's impact on VRR **must be treated as unverified until user testing**.
- Do not implement retries/state-machine frameworks or extra synchronization for theoretical races. A standard single Win32 message loop and bounded cleanup is sufficient for the experiment.

## 4. Logging — separate log file

At launch, create one standalone session log, for example:

```text
%LOCALAPPDATA%\SteamInputAddonforClaw-Data\logs\TouchpadPoc\touchpad-poc-YYYYMMDD-HHMMSS-P<pid>.log
```

Log file creation errors clearly; never pretend logging is active if creation failed. Print the **absolute log path** and the computed rectangle to the console.

Use an easy-to-read event-per-line format, including:
- timestamp (local ISO-8601 with offset) plus monotonically increasing timestamp/elapsed time, sequence index;
- phase/event: `Startup`, `Down`, `Move`, `Up`, `CaptureLost`, `Cancel`, `Shutdown`, `Error`;
- `PointerId`, `PointerType`, flags, physical `ScreenX/Y`, `LocalX/Y`, primary pointer indicator and (if available) capture status;
- tool version/OS details if available without dependencies, `DpiAwareness`, `PrimaryBounds`, `RegionRect`, `Hwnd`, requested/actual EXSTYLE, and fatal errors with Win32 last-error codes.

Keep `DOWN`, `UP`, capture-loss and actual failures **lossless**; `MOVE` may be bounded/coalesced for log volume only if raw pointer handling remains correct. No high-rate hardware polling or per-move console spam. Flush on shutdown; bounded flushing is preferable to a GPU/scheduler-affecting heavy logging loop.

Suggested console summary:

```text
TouchpadPoc — single bottom-right zone
Screen: 1920x1200 physical pixels (DPI aware)
Zone: X=960 Y=600 W=960 H=600 (bottom-right 25%)
Touch window: created; HWND=...
Log: C:\Users\...\AppData\Local\SteamInputAddonforClaw-Data\logs\TouchpadPoc\...
Exit: Ctrl+C, or automatic timeout
WARNING: this PoC intercepts touch over the bottom-right taskbar/tray area until it exits.
```

## 5. Safe bounded runtime

The region includes the bottom-right Windows taskbar/system tray. Avoid an unbounded accidental input trap:

- Provide a simple **default 90-second bounded session** with automatic close and clean `DestroyWindow`/unregistration. A narrow `--duration-seconds N` CLI option may be implemented if justified, with a safe maximum, but do not build a general CLI.
- `Ctrl+C` on the console must request a clean exit and close the window. Make console shutdown and the Win32 message loop cooperate without busy waiting or unusual synchronization.
- Always log `Shutdown` and final number of Down/Move/Up/CaptureLost events where logging is functional.
- Do not alter the touchscreen device or register a global OS touch sink. Terminating this process must release its HWND capture automatically.
- Show fatal startup errors (DPI/window creation/log open/message loop) and return nonzero rather than running a half-initialized diagnostic.

## 6. Offline tests / code correctness

Do local deterministic checks, without requiring a real touchscreen:
- Rectangle calculation for 1920x1200 and e.g. odd-sized screens and 150% scaling assumptions.
- Coordinate transformation, bounds/clamping if used.
- Pointer ID accounting with Down/Move/Up, missing Up on capture loss, two simultaneous fingers in the single zone.
- Log formatting and escape/control of error fields as appropriate; bounded operation/clean disposal.
- Verify the actual Win32 signatures, struct layout and 32-/64-bit casts against Win32 header/documentation.
- Build standalone Release and applicable repository tests (do not cause the normal product solution/CI to require Windows interactive desktop).
- Verify no production source, solution/package/release pipeline, or controller behavior changed.

**No live MSI Claw, game, touchscreen, VRR, Steam, PresentMon, or Special K testing by local Codex.** No hardware gate for PR review or CI. Document any limitations accurately.

## 7. User's post-merge manual experiment (information only, not a code review gate)

1. On the normal Windows desktop launch the PoC, confirm console rectangle and logfile.
2. Touch **inside bottom-right quarter**: quick taps, hold, drag, two fingers. Verify `DOWN/MOVE/UP`/IDs in log.
3. Touch outside (e.g. left side/top-right): ordinary desktop interactions should work, with no PoC touch events.
4. While the PoC is active, inspect whether the bottom-right Windows taskbar, mouse clicks or desktop icons are still hit by forwarded/promoted touch (an input-isolation failure). **Seeing an event in the log alone is insufficient evidence of suppression.**
5. Optional next experiment: activate PoC over a running borderless game; verify no in-zone game touch/click/mouse promotion and no game focus loss.
6. Compare VRR: PoC OFF / PoC ON without touch / PoC ON during repeated touch using ClawHUD's proven PresentMon + D3DKMT/appropriate physical variable-rate observation methods. `Hardware Composed: Independent Flip` by itself does **not** prove active VRR.
7. Record any visually apparent opaque backing/window, fullscreen-exclusivity limitation or fixed-refresh change as a PoC finding.
8. Let it auto-exit (90 seconds) or Ctrl+C; verify normal bottom-right desktop touch restores.

These measurements determine the **next architectural choice**. No claim that the candidate HWND always works or preserves VRR is allowed before results exist.

## 8. Done / strictly excluded

The PR is code-complete when the **one-window** diagnostic builds, creates and logs the exact bottom-right physical quadrant, handles touch pointer lifecycle properly, remains pixel-unrendered by design, shuts down cleanly, contains minimal run instructions, and does not modify product behavior. Report the automated checks accurately.

**Explicitly OUT:** second left-side window, 2x zones, settings editor, global toggle, Steam per-game UI, Overlay, VIIPER, Steam Deck/Edge mapping, click/gesture synthesis, optional vibration, live physical testing as a blocker, VRR/Independent Flip claims of success without observations.
