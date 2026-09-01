# Addon Quick Settings Overlay Architecture

> **Status:** Current design baseline / implementation planning document  
> **Date:** 2026-09-01  
> **Scope:** A dedicated Addon-owned handheld Quick Settings overlay that replaces the need to use Xbox Game Bar as the Addon's quick-settings surface.  
> **Implementation state:** This document defines the intended architecture. It does **not** claim that the overlay process, input-capture path, or Full PID1902 integration is already implemented or hardware-validated.  
> **Important:** Final WING/OEM1 button assignment is intentionally deferred. The overlay architecture must not depend on which physical MSI button ultimately toggles it.

---

## 1. Design authorities

Read this document together with the current controller architecture and work orders:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md`
- `docs/work-order/PR3_REBOOT_BOUND_CONTROLLER_AUTHORITY_TRANSITION_WORK_ORDER.md`
- `docs/VIIPER_IMPLEMENTATION_RULES.md`
- `docs/VIIPER_INTEGRATION.md`
- `docs/VIIPER_MIGRATION_TODO.md`

Current source seams relevant to this design include:

- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- `src/SteamInputAddonforClaw/Lifecycle/QamHostProcessController.cs`
- `src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs`
- `src/SteamInputAddonforClaw.UI/App.xaml.cs`
- `src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj`
- `src/SteamInputAddonforClaw/Input/IControllerStateSnapshotSource.cs`
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs`
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckOutputStage.cs`

The project is pre-release. Do not preserve obsolete Game Bar or Steam-QAM integration behavior merely for compatibility if it conflicts with the new product direction.

---

## 2. Product goal

Provide a native Addon-owned handheld Quick Settings panel that can be opened over a game without changing controller identity or handing UI ownership to Xbox Game Bar or Steam GamepadUI.

The intended user experience is deliberately simple:

```text
Game / Windows desktop
        ↓
physical quick-settings button
        ↓
Addon Quick Settings panel appears on the left
        ↓
controller navigation changes TDP / CPU Boost / FPS / Power Mode / future device controls
        ↓
close panel
        ↓
return immediately to the same game/controller presentation
```

The overlay is a **transient UI surface**, not a controller presentation, not a controller authority, and not another hardware owner.

Its existence must not change:

- Center M authority;
- desired physical PID;
- DirectInput ownership;
- HidHide ownership;
- VIIPER server/bus ownership;
- which virtual controller is selected by the Full PID1902 presentation policy.

---

## 3. Core design decision

The current preferred architecture is:

```text
SteamInputAddonforClaw.exe
    Addon Runtime
    ├─ controller authority
    ├─ PID1902 / DirectInput
    ├─ ControllerState
    ├─ HidHide
    ├─ VIIPER presentation owner
    ├─ Device/Profile feature authorities
    ├─ overlay capture authority
    └─ overlay process lifecycle / IPC endpoint
             │
             │ dedicated QAM/overlay IPC
             ▼
SteamInputAddonforClaw.Overlay.exe
    dedicated WinUI 3 process
    ├─ one left-side opaque Quick Settings window
    ├─ controller-driven logical navigation
    └─ presentation only; no hardware ownership

SteamInputAddonforClaw.UI.exe
    existing settings frontend
    └─ remains unchanged in its normal lifetime model
```

The overlay should be a **separate executable/process** from both the headless Runtime and the existing main WinUI frontend.

This is preferred over adding `OverlayWindow` to `SteamInputAddonforClaw.UI.exe` because the current main UI has a clean and already-established lifecycle:

```text
MainWindow closes
→ frontend pipe disconnects
→ UI process exits
```

Changing the main UI into a hidden persistent UI host only to support QAM would broaden its lifecycle responsibilities and couple QAM reliability to the main settings frontend.

A dedicated process keeps those concerns isolated.

---

## 4. Why a dedicated overlay process is preferred

### 4.1 Preserve the current main UI unchanged

The existing frontend is already disposable and separate from the Runtime. That is an important Full PID1902 property.

The overlay should not require rewriting the existing UI shutdown, single-instance, activation, diagnostic-session, or frontend-disconnect lifecycle simply to keep one hidden QAM window alive.

Preferred boundary:

```text
Main UI process
→ ordinary settings application lifecycle

Overlay process
→ persistent/warm quick-settings presentation lifecycle

Runtime process
→ durable controller/device authority lifecycle
```

### 4.2 Failure isolation

A Quick Settings frontend will eventually contain:

- many XAML controls;
- navigation state;
- live device/profile values;
- slider/toggle/dropdown mutations;
- animations or transitions;
- controller-focused visual state.

A fatal XAML/dispatcher/UI failure in this surface should not terminate the main settings UI and must never terminate the controller Runtime.

Desired failure boundary:

```text
Overlay.exe crashes
    ↓
Runtime survives
Main UI survives or remains independently launchable
current controller presentation remains owned by Runtime
Runtime cancels any active overlay capture
```

This is a real reliability benefit, not theoretical process isolation.

### 4.3 Independent iteration

The overlay is likely to receive substantial UI iteration after the base controller platform is stable.

Keeping it separate means Quick Settings layout/navigation changes can be reviewed without rewriting the main settings application's established startup/close path.

### 4.4 The cost is explicit

A separate warm WinUI process consumes additional memory compared with hosting the overlay in the existing UI process.

That cost must be measured on real MSI Claw hardware rather than assumed away.

The architecture chooses isolation and immediate-show latency first, with an explicit POC performance gate before the feature is treated as final.

---

## 5. Renderer and UI technology

### 5.1 Preferred technology: WinUI 3 + minimal Win32 HWND interop

The overlay should use WinUI 3 because:

- the project already ships a WinUI 3 frontend;
- the required UI is ordinary controls, not a high-frequency HUD renderer;
- sliders, toggles, dropdowns, icons, localization, scrolling, and layout are all normal XAML concerns;
- the overlay is opaque and rectangular;
- no per-pixel transparent composition surface is required;
- no custom Direct2D/DirectComposition renderer is needed for this product shape.

The Win32 portion should remain small and only handle top-level window behavior that WinUI alone does not express conveniently.

Conceptually:

```text
WinUI 3 XAML controls
        +
ordinary top-level HWND
        +
minimal Win32 style/position calls
```

### 5.2 Explicitly not required

Do not introduce the following merely because the feature is called an overlay:

- WPF as a second UI framework;
- `Windows.UI.Composition` as a separate rendering architecture;
- Direct2D renderer;
- DirectComposition renderer;
- DXGI injection;
- game-process DLL injection;
- hidden owner-window framework;
- click-through fullscreen transparent canvas;
- swappable `IOverlayHost` renderer abstraction;
- native rendering helper process;
- a general-purpose overlay engine.

Those designs solve a broader HUD/compositor problem than this feature has.

If real hardware proves an ordinary WinUI HWND cannot meet the required game compatibility or latency, revisit the smallest proven deficiency later. Do not pre-build the fallback architecture.

---

## 6. Visual/window contract

The Quick Settings window is intentionally simple.

### 6.1 Geometry

Target layout:

```text
┌───────────────┬─────────────────────────────────────────────┐
│               │                                             │
│               │                                             │
│     ADDON     │                                             │
│     QUICK     │                  GAME                       │
│   SETTINGS    │                                             │
│               │                                             │
│               │                                             │
├───────────────┴─────────────────────────────────────────────┤
│                    Windows taskbar                          │
└─────────────────────────────────────────────────────────────┘
```

Requirements:

- left aligned;
- opaque background;
- top aligned to the monitor working area;
- bottom aligned exactly to the monitor working area;
- must not cover the taskbar;
- width is a Quick Settings design dimension determined by actual control/icon layout;
- no requirement for full-screen transparency behind the panel;
- no requirement for arbitrary-shaped or floating window regions.

### 6.2 Use the monitor working area

Do not hard-code taskbar height or assume the primary monitor.

On show, resolve the intended monitor and use its current working area (`rcWork` conceptually):

```text
X      = WorkArea.Left
Y      = WorkArea.Top
Width  = QamPanelWidth
Height = WorkArea.Bottom - WorkArea.Top
```

This naturally respects a normal taskbar located at the bottom, top, left, or right.

For the supported handheld product environment, a single internal display is the dominant case, but implementation should use the real monitor working area rather than embedding that assumption in geometry code.

### 6.3 Target monitor

Preferred selection when opening:

1. monitor containing the current foreground game/window;
2. otherwise the monitor containing the active shell/foreground window;
3. otherwise primary monitor.

Do not add a persistent multi-monitor policy manager for the unsupported/pathological cases. A fresh monitor lookup on every show is enough.

### 6.4 Window characteristics

The intended top-level window behavior is approximately:

```text
borderless
not resizable by user
not shown in taskbar
TopMost
WS_EX_NOACTIVATE or equivalent no-activation behavior
SWP_NOACTIVATE when positioning/showing
```

Exact Win32/AppWindow implementation details belong in the implementation work order/POC.

The important product behavior is:

> Showing Quick Settings must not intentionally steal foreground ownership from the game.

---

## 7. Focus and controller-navigation model

The overlay must not depend on ordinary keyboard focus or foreground-window activation for controller navigation.

### 7.1 Runtime owns physical controller input

Under Full PID1902 Addon authority:

```text
physical MSI Claw PID1902
        ↓
DirectInput
        ↓
ControllerState
        ↓
Addon Runtime
```

The overlay process must not open another DirectInput session for the same controller.

The overlay process must not read back the virtual X360/SteamDeck controller through XInput/GameInput as its navigation source.

The Runtime already owns the canonical physical input truth. Reuse it.

### 7.2 Do not stream raw ControllerState over IPC

The canonical publishers run against `IControllerStateSnapshotSource.LatestState` at high cadence. The existing Steam Deck publisher is a dedicated ~250 Hz production path.

Do **not** turn the overlay IPC into:

```text
ControllerState 250 Hz
→ JSON/named pipe
→ Overlay.exe
```

The Quick Settings UI only needs semantic navigation events.

### 7.3 Semantic overlay navigation

While overlay capture is active, Runtime should translate physical state edges/repeat into low-rate UI commands, conceptually:

```text
D-pad / left stick up     → NavigateUp
D-pad / left stick down   → NavigateDown
D-pad / left stick left   → AdjustLeft / NavigateLeft
D-pad / left stick right  → AdjustRight / NavigateRight
A                         → Accept
B                         → Back / Close according to current page depth
LB                        → PreviousSection
RB                        → NextSection
physical overlay button   → CloseOverlay
```

Exact bindings can evolve with UI design, but the transport should carry semantic commands rather than raw reports.

### 7.4 Logical selection, not foreground focus

Because the overlay is intended to avoid activating itself, controller navigation should maintain an explicit **logical selected item** in the overlay UI and render its selected/highlighted state.

Do not require `Window.Activate()` simply so XAML keyboard focus can move.

A normal WinUI focus primitive may still be used internally if it can operate without breaking the foreground contract, but the architecture must not depend on the game losing foreground ownership.

---

## 8. Overlay capture is not a third virtual presentation

This is the most important controller invariant in this document.

Full PID1902 presentation policy is:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

Opening Quick Settings must **not** create another presentation selection rule.

Wrong design:

```text
SteamDeck active
→ Overlay opens
→ detach SteamDeck
→ attach X360
→ Overlay closes
→ detach X360
→ reattach SteamDeck
```

That recreates the historical nested Game Bar presentation lifecycle that Full PID1902 explicitly intends to replace.

Correct design:

```text
current presentation = Xbox360 or SteamDeck
        ↓
Overlay opens
        ↓
same presentation remains structurally selected/attached
        ↓
game-facing input becomes neutral while overlay owns navigation
        ↓
Overlay closes
        ↓
same presentation resumes live input
```

No overlay open/close operation should mutate:

- PID1901/PID1902;
- DirectInput acquisition;
- HidHide target/configuration;
- VIIPER server;
- VIIPER bus;
- X360/SteamDeck selection;
- Steam/BPM state.

---

## 9. Neutral-output contract while the overlay is open

### 9.1 Initial policy: full game-input neutralization

For the first implementation, while Quick Settings is consuming controller navigation, the game-facing virtual controller should receive neutral input.

This is preferred over selective passthrough because it is predictable and avoids a second mapping policy.

```text
Overlay hidden
→ physical ControllerState publishes normally

Overlay visible/capture active
→ physical ControllerState feeds OverlayInputRouter
→ selected virtual presentation remains attached
→ game-facing virtual output stays neutral
```

Do not start by implementing per-button passthrough rules.

### 9.2 Reuse the presentation owner's pause/neutral concept

Current Steam Deck code already contains a concrete pause primitive:

```text
stop publisher
→ write neutral
→ mark presentation paused
```

and a corresponding resume primitive.

That existing stage demonstrates the desired safety ordering, but the final custom overlay must not be permanently coupled specifically to `CanonicalSteamDeckOutputStage` because Full PID1902 also uses X360 as the non-Steam presentation.

The final owner should expose the smallest presentation-agnostic operation needed by overlay capture, conceptually:

```text
PauseCurrentPresentationForOverlayAsync()
ResumeCurrentPresentationAfterOverlayAsync()
```

Exact naming is not mandated.

Do not create a second presentation manager solely for the overlay. This behavior belongs on/through the one Full PID1902 presentation owner.

---

## 10. Open sequence

The open path must avoid the most user-visible failure: controller input becoming neutral when no usable overlay actually appeared.

Preferred sequence:

```text
physical overlay toggle event
        ↓
Runtime verifies OverlayHost process/IPC is ready
        ↓
if main settings UI is visible/running, request its normal close
        ↓
verify the mutually-exclusive UI surface condition is satisfied
        ↓
Runtime sends Show with fresh Quick Settings snapshot
        ↓
Overlay resolves monitor + WorkArea and shows the window no-activate
        ↓
Overlay acknowledges Visible/Ready
        ↓
Runtime enters OverlayCapture
        ↓
current virtual presentation neutralizes
        ↓
Runtime semantic navigation is delivered to Overlay
```

If the overlay process cannot start/connect/show:

```text
no capture commit
no persistent neutral output
game/controller remains live
log feature-local failure
```

The physical WING/OEM trigger is expected to be an MSI semantic/WMI button path rather than an ordinary face-button state, so waiting for a visible acknowledgement before full controller capture should not require adding a complex pre-show controller barrier.

If hardware evidence later proves the trigger leaks into ordinary virtual input, solve that concrete trigger path narrowly.

---

## 11. Close sequence and release-to-resume

Closing Quick Settings must not replay the close/Accept/navigation input into the game.

Preferred sequence:

```text
Close requested
    ↓
Overlay stops accepting navigation mutation
    ↓
Overlay hides window
    ↓
Runtime keeps current virtual presentation neutral
    ↓
wait until controls consumed by overlay are released / neutral
    ↓
clear OverlayCapture
    ↓
resume live publication on the SAME current presentation
```

This release-to-resume latch is required for practical handheld UX.

Example without the latch:

```text
B closes overlay
→ virtual publication resumes while B is still held
→ game also receives B
```

That is a normal user-visible failure and should be prevented.

Do not add epochs/barriers for hypothetical instruction-level crossings. A simple held-control release gate around the real close path is sufficient.

---

## 12. Overlay process lifecycle

### 12.1 Preferred baseline: warm hidden process

Quick Settings should feel immediate.

Preferred lifecycle:

```text
Addon Runtime becomes ready
        ↓
start Overlay.exe
        ↓
initialize Windows App SDK / XAML
        ↓
connect overlay/QAM transport
        ↓
create window
        ↓
hide window
        ↓
remain idle/warm
```

Then:

```text
button press → Show
close        → Hide
button press → Show
```

Do not create/destroy the process and XAML application for every panel toggle.

### 12.2 No supervisor/watchdog in the initial design

A warm overlay process can crash. That does not justify a general process supervisor.

Initial recovery:

```text
Overlay process dies while hidden
→ Runtime/controller unaffected
→ next overlay request may start a fresh Overlay.exe
```

and:

```text
Overlay process dies while capture active
→ Runtime observes process/IPC disconnect
→ cancel overlay capture
→ release-to-resume current presentation safely
```

No heartbeat protocol is required merely to detect a named-pipe/process disconnect.

### 12.3 Runtime shutdown

On controlled Runtime shutdown:

```text
stop accepting overlay open requests
→ if capture active, retire capture/neutral state safely
→ request Overlay.exe shutdown
→ bounded graceful wait
→ terminate disposable overlay process only if required
→ continue normal Runtime/controller teardown
```

Overlay cleanup must never become the owner of controller teardown.

---

## 13. Existing QAM process/transport foundation to reuse

Current main already has useful infrastructure that changes the preferred implementation approach.

### 13.1 There are already separate desktop and QAM endpoints

`FrontendPipeEndpoint` currently exposes:

```text
CreateForCurrentUser()
CreateQamForCurrentUser()
```

`AddonProcessHost` already creates two independent `NamedPipeAddonFrontendServer` instances:

```text
Desktop frontend endpoint
QAM frontend endpoint
```

This means the new overlay does **not** need to make the desktop frontend pipe multi-client and does **not** need to invent a third full settings transport merely because it is a separate process.

### 13.2 Current QamHost is already out-of-process and failure-local

`SteamInputAddonforClaw.QamHost` is intentionally a separate process that talks to the Runtime through the dedicated QAM endpoint.

Its current implementation is Steam GamepadUI/CEF-specific, but the process-isolation principle is directly reusable:

```text
QAM/frontend UI failure
→ no controller/VIIPER ownership transfer
→ Runtime remains authoritative
```

### 13.3 Reuse the QAM endpoint concept, not the Steam CEF implementation

The new custom overlay should be a new WinUI frontend process, not a WinUI layer added into the current CEF JavaScript QamHost.

Preferred migration direction:

```text
current:
SteamInputAddonforClaw.QamHost.exe
→ Steam CEF/GamepadUI integration
→ dedicated .Qam Runtime endpoint

future:
SteamInputAddonforClaw.Overlay.exe
→ native WinUI Quick Settings
→ reuse the dedicated QAM/overlay Runtime endpoint concept
```

Do not run the old Steam QamHost and the new OverlayHost as competing clients for the same single-instance QAM pipe.

Cutover must give one production owner to that endpoint.

### 13.4 Typed Runtime control can be reused

Because the overlay is C#, it can use the existing typed frontend transport/contracts instead of recreating a JavaScript bridge.

However, the overlay UI should expose only the subset of operations it actually needs.

A small overlay-side wrapper such as:

```text
OverlayRuntimeClient
```

is reasonable if it simply narrows the existing typed client to Quick Settings operations.

Do not create a second TDP/CPU/FPS/Power implementation inside the overlay.

### 13.5 Low-rate navigation notifications still need a narrow seam

The current general frontend transport primarily supports RPC plus `StateInvalidated` notification.

Overlay navigation needs low-rate Runtime → Overlay semantic events.

The implementation may extend the existing QAM endpoint with a small overlay-navigation notification seam, but must preserve these rules:

- no raw 250 Hz controller-state stream;
- no generalized event bus;
- no duplicate controller input reader;
- desktop frontend does not need to receive overlay navigation;
- pipe disconnect remains sufficient evidence that the overlay frontend disappeared.

Exact wire shape is intentionally left for the implementation work order because the product contract is the semantic command flow, not a specific serializer/class hierarchy.

---

## 14. Main UI and Quick Settings are mutually exclusive

The product should not show the main settings UI and Quick Settings overlay at the same time.

Desired visible-surface rule:

```text
MainUIVisible XOR OverlayVisible
```

This is a UX/product rule, not a new global authority state machine.

### 14.1 Opening Overlay while main UI is open

```text
overlay button pressed
        ↓
request normal Main UI close
        ↓
wait for Main UI to retire its frontend session
        ↓
show Overlay
```

Do not immediately hard-kill the main UI merely for normal handoff; it may own a transient diagnostic session that its established shutdown path already cleans up.

If the UI cannot close cleanly, fail the overlay open request rather than creating two simultaneous control surfaces. The game-facing controller must remain usable.

### 14.2 Opening main UI while Overlay is visible

```text
tray / shortcut / frontend-open request
        ↓
close Overlay
        ↓
complete release-to-resume
        ↓
launch/show existing Main UI normally
```

The existing main UI process lifecycle should remain unchanged.

### 14.3 Why this helps

Mutual exclusion removes a large class of unnecessary UI-level coordination problems:

- two visible TDP sliders fighting each other;
- two user edits racing visually;
- overlay and main UI showing different transient draft values;
- need for a cross-frontend edit authority manager.

Underlying Runtime feature implementations remain the only actual setting/hardware authorities.

---

## 15. Settings/device-feature ownership

The overlay is presentation only.

All mutations must still flow into existing Runtime-owned feature authorities, for example:

```text
Overlay TDP control
    ↓
Runtime TdpRuntime
    ↓
existing hardware/persistence path
```

```text
Overlay CPU Boost
    ↓
Runtime CpuBoostRuntime
```

```text
Overlay Power Mode
    ↓
Runtime PowerModeRuntime
```

```text
Overlay FPS limit
    ↓
Runtime IntelFrameLimiterRuntime / profile authority
```

Do not implement:

- `OverlayTdpManager`;
- `OverlayCpuBoostManager`;
- overlay-owned settings files;
- overlay-owned EC/driver/native helper access;
- duplicate profile mutation logic.

Where existing frontend DTOs already accurately represent the same fact, prefer reuse over creating a second semantically identical model.

---

## 16. Device mode vs active-game profile behavior

The current Steam QAM bridge distinguishes device-level mutation in BPM/no-game from active-game profile mutation during a game.

The custom Quick Settings design should preserve the product meaning rather than blindly preserve the current JavaScript bridge implementation.

A future overlay page may show, for example:

```text
no active game
→ Device controls

active recognized game/profile
→ current game profile controls
```

or another explicitly designed UI model.

That UX is not frozen by this architecture document.

What **is** frozen:

- the Overlay never becomes the data authority;
- active game/device facts come from Runtime;
- mutation result/readback comes from Runtime;
- the UI should not infer success because a control changed visually.

---

## 17. Game Bar policy

### 17.1 Game Bar is not the new overlay host

The custom Addon Quick Settings panel replaces the need to use Xbox Game Bar as the Addon's handheld quick-settings surface.

Opening Quick Settings must never trigger:

```text
Win+G
Xbox Game Bar foreground
GameBarForegroundWatcher presentation selection
SteamDeck → X360 presentation switch
```

The historical Game Bar/X360 nested presentation experiment remains historical and is not part of this architecture.

### 17.2 Native Game Bar activation from the chosen Addon quick-settings button must be suppressed

Once a physical MSI button is assigned as the Addon Quick Settings button in Addon-owned controller mode, its native MSI/Win+G path must not also open Game Bar.

This is a concrete double-action bug to prevent:

```text
physical QAM button
→ Addon Overlay opens
AND
→ Xbox Game Bar opens
```

The existing Win+G suppression foundation should be reused where applicable rather than introducing another keyboard-hook authority.

### 17.3 Do not uninstall/disable Xbox Game Bar globally

The product requirement is to prevent the Addon-owned button path from invoking Game Bar while the Addon owns that interaction.

Do not require globally uninstalling Xbox Game Bar or applying broad Windows policy changes merely to implement this feature.

### 17.4 Center M Enabled / stock authority boundary

This document primarily defines the Full PID1902 Addon-owned mode.

Whether stock/Center M Enabled mode should also suppress a physical button's native Game Bar behavior is a separate product-policy decision and should follow the final button/authority contract.

Do not silently broaden Addon button ownership into MSI-stock authority merely because the hook exists.

---

## 18. WING / OEM1 mapping is intentionally deferred

The overlay architecture must expose one semantic operation:

```text
ToggleAddonQuickSettings()
```

The physical-button policy decides what invokes it.

Do not bake WING or OEM1 assumptions into `Overlay.exe`, the IPC protocol, or the presentation capture logic.

### 18.1 Latest candidate discussed, not final

The latest ergonomic candidate is:

```text
Center M Disabled / Addon-owned controller

                         Non-Steam / X360      Steam/BPM / SteamDeck
--------------------------------------------------------------------
WING (left)              user mapping           Steam Button
OEM1 (right)             Addon Overlay          Addon Overlay
Game Bar                 not invoked            not invoked
```

Rationale:

- left button preserves the Steam-button position during Steam usage;
- right button behaves like a persistent handheld Quick Access key;
- Quick Settings remains available in both Steam and non-Steam contexts;
- Steam Button does not need to be sacrificed;
- Steam Quick Access physical binding becomes less important because Addon Quick Settings replaces its primary quick-control role.

This table is **not a frozen contract** yet.

Possible final button policy changes must not require redesigning the overlay process architecture.

### 18.2 Avoid single/double timing just to fit both Steam and overlay on one button

A QAM surface should appear immediately.

Do not choose a design that delays every normal QAM open simply to wait for a double-press timeout unless later product testing demonstrates a compelling reason.

A stable one-button/one-primary-action rule is preferred for the overlay trigger.

---

## 19. Steam Button and Steam Quick Access primitives

The custom Addon overlay does not require deleting the existing Steam Deck synthetic system-button primitives.

Current code already has:

```text
RequestSteamPulse()
RequestQuickAccessPulse()
```

on the canonical Steam Deck path.

Even if a future physical-button default stops exposing Steam Quick Access directly, the internal capability may remain useful.

Do not delete it as part of the initial overlay work unless a later cleanup PR proves it is dead after the product migration.

Likewise, do not make Overlay visibility change the Steam/BPM presentation selection.

---

## 20. Presentation and overlay state interaction

The conceptual state is intentionally small.

Stable controller state:

```text
CurrentPresentation = Xbox360 | SteamDeck
OverlayCapture      = Off | On
```

These are orthogonal facts.

Examples:

```text
Xbox360 + OverlayCapture Off
→ normal non-Steam controller

Xbox360 + OverlayCapture On
→ Xbox360 remains selected; output neutral; overlay consumes navigation

SteamDeck + OverlayCapture Off
→ normal Steam controller

SteamDeck + OverlayCapture On
→ SteamDeck remains selected; output neutral; overlay consumes navigation
```

Do not encode combined states such as:

```text
SteamDeckOverlayMode
XboxOverlayMode
GameBarOverlayMode
```

The presentation owner knows the presentation. Overlay capture only gates whether live physical input is currently published to it.

---

## 21. Presentation change while Overlay is open

A real product event can occur while the overlay is open: Steam/BPM state may change.

The desired rule remains simple:

> Overlay capture stays authoritative for user input while visible; the Full PID1902 presentation owner may reconcile the desired presentation, but no live physical input is published to the game until overlay capture ends.

Example:

```text
Overlay open on Xbox360
→ Steam game becomes active
→ presentation owner changes desired output to SteamDeck according to normal Full PID1902 policy
→ new current presentation remains neutral because OverlayCapture is still active
→ Overlay closes
→ release gate completes
→ SteamDeck resumes live input
```

Do not add a special overlay presentation state machine.

The presentation owner's normal serialization/reconcile path remains authoritative.

This path should be implemented only when the Full PID1902 X360↔SteamDeck owner exists; do not bolt it onto obsolete route-scoped policy prematurely.

---

## 22. Overlay crash while active

This is a required real failure path.

Failure:

```text
Overlay visible
→ current virtual output neutral
→ Overlay.exe crashes / pipe disconnects
```

Required Runtime response:

```text
detect overlay process/IPC loss
→ mark overlay surface unavailable
→ stop semantic navigation delivery
→ cancel overlay capture
→ wait for overlay-consumed controls to release where needed
→ resume live input on the current presentation
```

If the underlying physical source or presentation is independently unsafe at that moment, preserve the existing Full PID1902 fail-closed behavior instead of forcing a resume.

Overlay recovery must never override a real controller safety fault.

Do not auto-switch to another virtual controller because the UI process crashed.

---

## 23. Physical input loss while Overlay is open

If PID1902/DirectInput disappears while the overlay is open:

```text
Overlay remains a UI surface only
Runtime sees physical input loss
→ existing controller reconcile/fail-close owns recovery
→ virtual presentation stays neutral
```

The overlay may display an unavailable/controller-disconnected status if useful, but it must not attempt physical reacquisition.

When safe physical input returns and Full PID1902 reconcile succeeds, the overlay may continue or be closed according to the final UX decision.

Initial implementation may simply close the overlay after a real controller failure if that produces a simpler reliable contract.

Do not add overlay-specific PnP recovery machinery.

---

## 24. Sleep / hibernate / resume

Quick Settings is not an authority boundary.

Before suspend:

```text
if Overlay visible
→ hide/retire overlay interaction
→ keep controller lifecycle governed by the Full PID1902 suspend contract
```

After resume:

```text
Runtime reconciles controller authority / PID / DirectInput / HidHide / VIIPER first
→ Overlay process/window may be recreated/reconnected if needed
→ remain hidden
```

Do not automatically reopen Quick Settings after resume merely because it was visible before suspend.

That avoids stale monitor handles, stale UI state, and surprising post-resume foreground UI.

No separate overlay resume manager is needed.

---

## 25. Runtime restart and authority lifetime

Under Center M Disabled, Runtime is mandatory and controller authority survives controlled Runtime restart according to the Full PID1902 design.

Overlay behavior is subordinate:

```text
controlled Runtime restart
→ Overlay hides/exits or loses pipe
→ controller Runtime performs its normal controlled restart teardown/reconcile
→ new Runtime starts
→ new Overlay process may be started warm/hidden
```

Overlay must never attempt to keep old controller state alive across Runtime restart.

The Runtime is the authority boundary.

---

## 26. Opaque UI means no composition-specific requirement

The current design explicitly does **not** require:

- transparency;
- acrylic behind the game;
- blur of the game;
- click-through regions;
- per-pixel alpha;
- a fullscreen transparent top-level window.

The panel can use a normal solid WinUI background.

This materially reduces implementation risk and is one reason the preferred solution is ordinary WinUI 3 rather than WPF/DirectComposition/native rendering.

Visual polish may still use normal WinUI colors, shadows, rounded control surfaces, and transitions as long as those remain ordinary UI concerns.

---

## 27. Input support priority

Initial input priority:

1. physical MSI controller;
2. optional touch/pointer if it works naturally with the no-activate window contract;
3. keyboard/mouse only if useful later.

Controller operation is the product requirement.

Do not weaken the no-focus game UX merely to obtain ordinary keyboard navigation in the first implementation.

If pointer/touch activation causes the game to lose foreground, treat that as a separate UX tradeoff rather than changing controller navigation architecture.

---

## 28. Performance requirements and POC gate

The overlay is not an always-on HUD. Its important performance properties are different.

### 28.1 Measure, do not assume

Before finalizing the WinUI-process choice, measure on actual supported MSI Claw hardware:

```text
Overlay.exe hidden idle CPU
Overlay.exe hidden working set / private bytes
Overlay.exe first initialization cost
warm hidden → first visible frame latency
Show → Hide → Show latency
foreground game focus before/after Show
frametime/VRR effect while hidden
frametime/VRR effect while visible
```

### 28.2 Desired behavior

Hidden:

```text
no controller-state polling loop in UI
no animation loop
no high-rate telemetry loop unless explicitly required by visible controls
negligible CPU activity
```

Visible:

```text
normal WinUI rendering
low-rate value updates
controller semantic events only
```

### 28.3 Cold start is not the normal toggle path

A cold process/XAML start every time the user presses QAM is not the desired UX.

The warm hidden model exists specifically to remove:

```text
process creation
Windows App SDK initialization
XAML initialization
pipe handshake
window construction
```

from the normal button-to-panel path.

If memory measurements later make a warm process unacceptable, revisit a lazy-start/cache strategy with evidence. Do not prematurely optimize away responsiveness.

---

## 29. Fullscreen/game compatibility target

An ordinary topmost desktop HWND cannot promise universal coverage over every true exclusive-fullscreen presentation mode.

Initial product target:

```text
Windowed games                      required
Borderless fullscreen               required
modern flip-model / optimized mode  hardware validation required
true legacy exclusive fullscreen    best effort / not guaranteed initially
```

Do not add game injection, graphics API hooks, or a custom swap-chain overlay only to defend against unsupported/rare fullscreen modes before real Claw testing establishes a meaningful problem.

If a concrete supported game mode consistently prevents the window from being visible, document that evidence and solve the specific rendering/windowing problem later.

---

## 30. Packaging

A new WinUI executable must not accidentally duplicate an entire unnecessary Windows App SDK payload in the release package.

The implementation work order should inspect the current publish layout and prefer reuse/shared placement of runtime assets where supported by the unpackaged WinUI deployment model.

Goals:

- one installer/release package;
- main Runtime remains `SteamInputAddonforClaw.exe`;
- main settings UI remains separately packaged as today;
- Overlay.exe is included as an internal product component;
- no second user-facing installer;
- no duplicate application registration merely because the process is separate.

Do not change framework/runtime deployment strategy without measuring the resulting package and validating WinUI startup on a clean machine.

---

## 31. Security and IPC scope

The existing frontend endpoints are user-scoped and use `PipeOptions.CurrentUserOnly`.

Preserve the same local-user boundary for the overlay/QAM endpoint.

The supported product model remains:

```text
1 Windows user
1 interactive session
```

Do not add multi-session routing, session arbitration, service brokers, or cross-user QAM ownership for unsupported Fast User Switching/RDP scenarios.

The overlay must not accept arbitrary external commands through a global/public endpoint.

---

## 32. Logging

Logging should be low-rate and lifecycle-oriented.

Useful events:

```text
OverlayHost process start/exit
Overlay IPC connected/disconnected
Overlay Show requested / visible confirmed
Overlay Hide requested / hidden confirmed
Overlay capture entered/exited
capture canceled because frontend disconnected
main UI ↔ overlay handoff
selected monitor/work-area summary at Show
mutation failure returned from Runtime
```

Do not log every navigation event at Info level.

Do not log raw ControllerState at 250 Hz.

High-rate diagnostic logging should require explicit developer diagnostics and must not be part of normal production behavior.

---

## 33. Main UI / Overlay surface handoff failure policy

### Overlay requested while Main UI refuses/fails to close

Preferred fail-safe:

```text
leave game/controller live
keep Overlay hidden
report/log UI handoff failure
```

Do not create simultaneous mutation surfaces by default.

### Main UI requested while Overlay cannot close cleanly

Runtime should first retire overlay capture safely.

If the overlay process is hung, process termination is acceptable **after** Runtime has reclaimed overlay input authority, because Overlay.exe is disposable UI. The exact bounded timeout belongs in the work order.

Controller safety must not depend on the overlay responding to a close message.

---

## 34. State refresh model

Quick Settings needs fresh values without becoming a polling-heavy monitoring application.

Preferred model:

```text
Show
→ capture one fresh aggregate Quick Settings snapshot

Runtime feature changes
→ low-rate StateInvalidated / targeted change notification
→ refresh relevant values

user mutation
→ Runtime returns authoritative mutation result + resulting snapshot/value
```

Do not add a constant high-frequency state polling loop merely because the panel is visible.

Some values may genuinely need periodic refresh if a later UI card displays rapidly changing telemetry. Add that only for the specific card and only at a rate justified by the user experience.

This Quick Settings architecture is not the ClawHUD telemetry architecture.

---

## 35. Recommended first Quick Settings scope

The architecture supports the existing device/profile controls, but the first UI should stay intentionally small.

Reasonable first-wave candidates are features already owned by Runtime and useful during gameplay:

- TDP;
- CPU Boost;
- Windows Power Mode;
- Intel FPS Limit;
- current game profile enable/state where meaningful;
- later fan/fan-curve controls once their production runtime contract is ready;
- later resolution/refresh controls only if their in-game mutation policy is proven safe.

The architecture does not require all of these in the first PR.

Do not delay the window/input POC until every feature card is designed.

---

## 36. Recommended implementation sequence

This is a design roadmap, not yet a set of executable work orders.

### OQ1 — Window/process POC

Goal: prove the rendering/window choice without touching controller publication.

```text
new WinUI Overlay.exe
single opaque left panel
working-area positioning
TopMost / no-activate behavior
Show/Hide command
hidden idle mode
```

Validate:

- game remains foreground;
- panel appears above target borderless/flip-model games;
- taskbar is not covered;
- DPI/layout correct on supported Claw display;
- memory/CPU/latency acceptable.

No controller neutralization yet.

### OQ2 — Process lifecycle + existing QAM endpoint migration

Goal: establish the dedicated overlay frontend boundary.

- reuse the existing dedicated QAM endpoint concept;
- do not modify desktop frontend into multi-client transport;
- replace/retire the current Steam CEF QamHost production owner when cutover occurs;
- keep main UI lifecycle unchanged;
- implement warm hidden process lifecycle;
- process/pipe disconnect remains feature-local.

No new controller authority.

### OQ3 — Overlay capture / neutral publication

Goal: make controller navigation safe.

- Runtime owns `OverlayCapture`;
- current presentation pause/neutral through the one presentation owner;
- semantic controller commands;
- opening-button suppression as needed;
- release-to-resume latch;
- crash/disconnect capture cancellation.

Do not switch X360/SteamDeck because the overlay opens.

### OQ4 — Quick Settings feature controls

Goal: add the actual useful cards through existing Runtime authorities.

Start with a small set such as TDP / CPU Boost / Power Mode / FPS.

No duplicate managers.

### OQ5 — Main UI mutual exclusion

Goal: polished surface handoff.

- Overlay request closes Main UI first;
- Main UI request closes Overlay first;
- no simultaneous visible control surfaces;
- safe failure behavior.

This may be combined with OQ2 if the diff stays small and clear.

### OQ6 — Physical button policy / Game Bar replacement

Only after the overlay itself is proven:

- bind the selected WING/OEM1 policy to `ToggleAddonQuickSettings()`;
- ensure native Game Bar activation does not accompany that button;
- preserve Steam Button according to the final mapping decision;
- update/remove obsolete Steam-QAM/GameBar assumptions in documentation/UI.

Do not let the unresolved physical-button choice block the earlier window/process POC.

### OQ7 — lifecycle hardware validation and cleanup

Validate:

- suspend/resume;
- overlay process crash hidden/visible;
- Runtime controlled restart;
- Steam/BPM transition while overlay is visible;
- physical input loss/re-enumeration while overlay is visible;
- release-to-resume input leakage;
- repeated rapid Show/Hide under real handheld usage.

Only after the new path is proven should obsolete historical Game Bar/QamHost code be removed in focused cleanup PRs.

---

## 37. Relationship to the current Full PID1902 roadmap

This design must not derail the current controller-authority PR sequence.

The Full PID1902 foundation still owns, in order, the difficult controller work:

```text
persistent HidHide
mandatory Runtime lifetime
authority transition
Disabled-boot admission
PID1902 / DirectInput ownership
first presentation
X360 ↔ SteamDeck presentation switching
real lifecycle recovery
```

The overlay window/process POC can be developed independently because it is UI-only.

However, **production controller capture/neutralization should integrate with the final Full PID1902 presentation owner**, not cement new dependencies on the old Steam-route-scoped lifecycle that Full PID1902 is replacing.

In other words:

```text
UI shell POC          can happen early
Runtime capture seam  should target the new owner architecture
```

---

## 38. Anti-overengineering rules

Apply the repository's normal review policy.

Do not add complexity for purely theoretical races.

Do not introduce, without concrete product evidence:

- `OverlayAuthorityManager`;
- `OverlayStateMachineFramework`;
- render-host abstraction hierarchy;
- overlay epoch/barrier system;
- heartbeat supervisor;
- process service;
- multi-user session broker;
- duplicated TDP/profile authority;
- controller-state message bus;
- generalized UI-surface arbitration framework.

Protect realistic failures:

- Overlay process crashes while game input is neutral;
- Overlay cannot show after the user requests it;
- Main UI/Overlay handoff fails;
- Runtime shuts down/restarts;
- sleep/hibernate/resume;
- physical device loss;
- presentation failure;
- actual setting mutation failure.

A simple owner/disconnect/release path is preferred when it safely covers those real lifecycle cases.

---

## 39. POC acceptance checklist

Before declaring the basic WinUI overlay architecture validated, hardware testing should prove at least:

### Window

- [ ] opens on the correct display;
- [ ] uses working-area height and does not cover the taskbar;
- [ ] remains left-aligned after reopen;
- [ ] does not unexpectedly resize the game;
- [ ] does not intentionally activate itself / steal game foreground;
- [ ] visible above representative borderless/fullscreen-optimized games;
- [ ] clean repeated Show/Hide.

### Performance

- [ ] hidden CPU effectively idle;
- [ ] hidden memory recorded;
- [ ] warm Show latency recorded;
- [ ] visible frametime impact checked;
- [ ] hidden frametime/VRR impact checked;
- [ ] no high-rate IPC from controller state.

### Controller capture

- [ ] game input neutral while overlay active;
- [ ] D-pad/stick navigation does not also move the game;
- [ ] Accept/Back do not leak into the game;
- [ ] close button does not leak on resume;
- [ ] same virtual presentation remains selected before/after overlay;
- [ ] no PID/HidHide/DirectInput churn.

### Failure

- [ ] killing Overlay.exe while hidden does not affect controller Runtime;
- [ ] killing Overlay.exe while visible restores safe game input if underlying controller state is healthy;
- [ ] failed overlay startup leaves controller live;
- [ ] Runtime shutdown does not hang on Overlay;
- [ ] resume does not unexpectedly reopen stale Overlay UI.

### UI coexistence

- [ ] opening Overlay closes/retires Main UI first;
- [ ] opening Main UI closes/retires Overlay first;
- [ ] both control surfaces are never intentionally visible together.

---

## 40. Current decisions vs deferred decisions

### Current design baseline

The following are current architecture decisions:

1. Quick Settings is Addon-owned, not Xbox Game Bar-hosted.
2. Use a dedicated `SteamInputAddonforClaw.Overlay.exe` process.
3. Use WinUI 3 for the UI.
4. Use minimal Win32 HWND interop only for top-level window behavior.
5. The panel is opaque, rectangular, and left-side.
6. Vertical size is the monitor working area; taskbar is not covered.
7. Width is a UI design dimension based on actual content.
8. Overlay process is preferably warm/hidden for low toggle latency.
9. Main settings UI and Quick Settings are mutually exclusive visible surfaces.
10. Runtime remains the only controller/device/settings authority.
11. Runtime owns physical `ControllerState`.
12. Overlay receives semantic navigation, not raw high-rate controller reports.
13. Opening Overlay never changes X360/SteamDeck selection.
14. Current selected virtual presentation is neutral while Overlay captures navigation.
15. Closing uses release-to-resume to prevent input leakage.
16. Overlay process/IPC loss while capture is active cancels capture safely.
17. Existing dedicated QAM endpoint/transport foundation should be reused rather than making the desktop frontend pipe multi-client.
18. The current Steam CEF `QamHost` implementation is a migration source/legacy frontend, not the desired final renderer.
19. Game Bar foreground is not a presentation-selection event.
20. The physical button assigned to Addon Quick Settings must not simultaneously invoke native Game Bar.

### Deferred product decisions

The following are deliberately not frozen here:

- final WING vs OEM1 assignment;
- exact normal/non-Steam WING user-mapping catalog after Full PID1902;
- whether Steam Quick Access remains user-selectable after custom QAM ships;
- exact first-page card order;
- exact panel width;
- exact controller focus visuals;
- touch/mouse support priority;
- whether an active physical-input failure closes the panel or leaves an unavailable panel visible;
- final removal timing for the old Steam CEF QamHost and dormant Game Bar/X360 experiment code.

These decisions can change without changing the architecture above.

---

## 41. Final target architecture

```text
                    FULL PID1902 ADDON RUNTIME
                              │
           ┌──────────────────┼────────────────────┐
           │                  │                    │
     Physical owner      Feature owners      UI orchestration
   PID1902/DirectInput   TDP/CPU/FPS/etc.   Overlay capture
           │                  │                    │
           └──────────┬───────┘                    │
                      │                            │
                ControllerState                    │
                      │                            │
         ┌────────────┴────────────┐               │
         │                         │               │
 Presentation owner          OverlayInputRouter    │
         │                         │               │
 X360 OR SteamDeck            semantic commands   │
         │                         │               │
 live when hidden                  └──────┬────────┘
 neutral when overlay active              │
                                          │ dedicated QAM/overlay IPC
                                          ▼
                           SteamInputAddonforClaw.Overlay.exe
                                  WinUI 3 opaque panel
                                  left WorkArea surface
                                  no controller authority
```

Main settings UI remains separate:

```text
SteamInputAddonforClaw.UI.exe
        ↕ existing desktop frontend endpoint
Addon Runtime
```

Visible UI policy:

```text
Main UI visible
        XOR
Quick Settings visible
```

Controller presentation policy remains independent:

```text
Steam/BPM inactive → X360
Steam/BPM active   → SteamDeck
```

Overlay policy is orthogonal:

```text
Overlay hidden → current presentation live
Overlay shown  → current presentation neutral + controller drives Quick Settings
```

---

## 42. Final design principles

1. **Quick Settings is a UI surface, not a controller presentation.**
2. **Opening Quick Settings must not switch X360 ↔ SteamDeck.**
3. **Runtime keeps all physical/controller/device authority.**
4. **Overlay.exe owns only WinUI presentation and transient logical selection.**
5. **Use the existing separate QAM transport concept rather than destabilizing the desktop frontend pipe.**
6. **Do not stream high-rate ControllerState to the UI process.**
7. **Neutralize the current presentation while the controller is navigating Quick Settings.**
8. **Release held overlay controls before returning live input to the game.**
9. **Overlay crash must never strand a healthy controller in permanent neutral state.**
10. **Main UI and Overlay remain mutually exclusive without creating a generalized UI authority manager.**
11. **Use a normal opaque WinUI HWND; do not build a compositor/HUD engine without evidence.**
12. **Respect the monitor WorkArea so the taskbar is never intentionally covered.**
13. **Game Bar is not the Addon's new quick-settings host and is not a presentation-selection event.**
14. **The final physical button mapping is a replaceable policy above the overlay architecture.**
15. **Measure hidden memory, idle CPU, show latency, foreground behavior, and game compatibility on real MSI Claw hardware before calling the renderer choice final.**
16. **Protect realistic handheld lifecycle failures, not theoretical instruction-level races.**
17. **Prefer one controller owner, one presentation owner, one settings authority per feature, and one disposable overlay frontend.**
