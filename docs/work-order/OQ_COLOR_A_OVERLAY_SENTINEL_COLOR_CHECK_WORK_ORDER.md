# Work Order — OQ-COLOR-A: Overlay Sentinel Color Check

## Goal

Determine whether the remaining full-size black-looking Overlay surface is
simply the current XAML `OpaquePanel` background, or a separate WinUI/
composition backing surface.

Current geometry is already correct (fixed by `OQ-FRAME-A`):

```
Window = 600 x 1128
Client = 600 x 1128
XAML   = 600 x 1128
Insets = 0
```

This PR does not touch geometry again.

## Change

`src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml`:

```xml
Background="#FF2B2B2B"  ->  Background="#FFFF00FF"
```

Magenta is intentional and temporary. No other production-code change.

## Hardware test

Launch the Overlay on the MSI Claw.

- **Result A** — the entire current black-looking area becomes magenta: the
  surface is `OpaquePanel`; no separate black compositor layer exists. Revert
  the sentinel color and choose the final intended gray in a follow-up change.
- **Result B** — magenta is visible but a black layer still covers part of it:
  a separate WinUI/composition/backing surface exists; investigate that layer
  next.

## Non-goals

`WindowInterop`, `WM_NCCALCSIZE`, HWND size/position, animation, opacity
animation, outside-click handling, no-activate behavior, Runtime/Overlay IPC,
controller/routing code, XAML margins/padding, Mica/Acrylic/transparency — all
unchanged. One-variable diagnostic only.
