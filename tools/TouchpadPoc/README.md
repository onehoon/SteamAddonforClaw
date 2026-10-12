# TouchpadPoc

TouchpadPoc is a standalone Windows 11 x64 diagnostic for one physical touchscreen region: the bottom-right quarter of the primary monitor. It creates one topmost WS_POPUP input-target HWND with no drawing or graphics path, records touch pointer events to a separate JSON-lines log, and exits after 90 seconds or on Ctrl+C. Whether Windows keeps the HWND visually absent and routes touch as intended must be confirmed during the manual experiment.

This is a feasibility experiment, not a production touchpad implementation. Receiving pointer messages does not prove that the same contact is isolated from a game or that the HWND preserves variable refresh rate (VRR). Those findings require the manual checks below.

## Build and run

From the repository root:

    dotnet build tools/TouchpadPoc/TouchpadPoc.csproj -c Release
    dotnet run --project tools/TouchpadPoc/TouchpadPoc.csproj -c Release

The process requests no elevation and accepts no command-line options. It declares per-monitor-v2 DPI awareness before querying the primary monitor. The single input region uses physical-pixel bounds; on a 1920 × 1200 display its expected rectangle is X=960, Y=600, Width=960, Height=600.

Logs are written to:

    %LOCALAPPDATA%\SteamInputAddonforClaw-Data\logs\TouchpadPoc\

Each JSON line has a local ISO-8601 timestamp with offset, elapsed milliseconds, and sequence number. Touch Down/Move/Up/Cancel/CaptureLost events include pointer ID/type, flags, predicted and raw physical screen/client coordinates when available, primary-pointer state, whether the message target is this HWND, and tracking state. That target field is not by itself proof of pointer capture. Windows may coalesce pointer updates before delivering them; the log records messages received by this HWND, not every digitizer sample. Move writes are buffered; state transitions and errors are flushed. Per-move console output is intentionally omitted.

The console warns that the topmost rectangle includes the bottom-right taskbar/system tray. While the process runs, touch or other pointer input aimed at that rectangle may not reach the underlying desktop. Outside the rectangle there is no TouchpadPoc HWND.

## Offline tests

    dotnet test tools/TouchpadPoc.Tests/TouchpadPoc.Tests.csproj -c Release

The tests cover quadrant geometry (including odd dimensions and non-zero origins), physical-to-client coordinates, pointer-ID lifecycle and capture loss, JSON escaping, the 90-second bound, and x64 Win32 structure sizes. They do not simulate the Windows input stack or substitute for touchscreen testing.

## Manual desktop experiment

1. Launch from the normal Windows desktop. Confirm the printed primary bounds, DPI awareness, zone rectangle, HWND, and absolute log path.
2. In the bottom-right quarter, try quick taps, a held touch, a drag, and two simultaneous fingers. Confirm pointer IDs and Down/Move/Up entries in the log.
3. Touch outside the rectangle (for example, the left side and top-right). Confirm there are no TouchpadPoc events and normal Windows interactions still work there.
4. While the PoC is active, check whether an in-zone touch also moves/clicks the taskbar, desktop, or a foreground app. A logged event alone is not proof that the touch was isolated.
5. Optionally repeat over a borderless game and inspect focus, touch/mouse promotion, and the game's input.
6. Compare VRR with the PoC off, on without touch, and on during touch. Independent Flip by itself does not prove active VRR.
7. Stop with Ctrl+C or let the 90-second timer expire; verify that in-zone desktop touch works again.

Do not use a physical-device result to claim production support without recording the tested Windows/display/game conditions. This PR contains no MSI Claw, game, touch-isolation, or VRR hardware result.
