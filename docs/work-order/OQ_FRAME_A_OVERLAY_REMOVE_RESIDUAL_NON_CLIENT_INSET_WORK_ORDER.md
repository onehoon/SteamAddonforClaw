# Work Order — OQ-FRAME-A: Remove Residual Overlay Non-Client Inset

## Status

Focused corrective PR for the Addon Quick Settings Overlay.

Label: `OQ-FRAME-A`

Follows the hardware diagnostic work from `OQ-LOG-B`. Not part of the numbered
Full PID1902 PR sequence.

## 1. Goal

Remove the real 3-physical-pixel non-client inset surrounding the WinUI 3 Overlay
client surface.

Hardware evidence (1920 x 1200, 150% scale, DPI 144, WorkArea 1920 x 1128):

```
WindowRect = 600 x 1128
ClientRect = 594 x 1122
Client origin = Window origin + 3,+3
Insets L/T/R/B = 3/3/3/3
```

The XAML surface already fills the client area correctly (proven by OQ-LOG-B).
The remaining defect is the size of the client area itself: a 3px non-client
frame on every edge.

Target:

```
WindowRect == ClientRect
Window = 600 x 1128
Client = 600 x 1128
Insets L/T/R/B = 0/0/0/0
```

## 2. Required fix — claim the full HWND as client area

Handle `WM_NCCALCSIZE` in the existing Overlay HWND subclass. When
`wParam == TRUE`, return `0` so the application uses the entire window region as
the client area.

- Do not mutate `NCCALCSIZE_PARAMS`.
- Do not compute or compensate for the current 3px inset.
- Do not enlarge the HWND to hide a frame.
- Do not touch XAML geometry, surface tone, or the animation.

## 3. Deterministic frame recalculation

Apply `SWP_FRAMECHANGED` on the final `SetWindowPos` placement call so Windows
recalculates the non-client/client layout once. No retry loop, no second resize
cycle, no delayed correction.

## 4. Scope

Production code change is confined to
`src/SteamInputAddonforClaw.Overlay/WindowInterop.cs`:

- `+ WmNcCalcSize` constant and switch case
- `+ SwpFrameChanged` constant applied to the final placement

No new class, service, interface, manager, or abstraction. DWM border/corner
attributes and layered/compositor workarounds are explicit non-goals.

## 5. Validation

`WM_NCCALCSIZE` full-client handling added narrowly; frame recalculation applied
deterministically; HWND target dimensions unchanged; no compensation math; no
XAML workaround. OQ-LOG-B bounds diagnostics retained as the acceptance oracle.

Regression: outside-click dismissal, inside-click non-dismissal, no-activate
contract, topmost behavior, WorkArea placement, uncovered taskbar, 32 DIP
animation, warm PID/HWND reuse, transport protocol, and Runtime visibility
authority all preserved.
