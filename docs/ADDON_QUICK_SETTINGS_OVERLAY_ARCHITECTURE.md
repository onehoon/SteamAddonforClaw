# Addon Quick Settings Overlay Architecture

> **Status:** Current design baseline / implementation planning document  
> **Date:** 2026-09-01  
> **Scope:** Addon-owned handheld Quick Settings overlay, its process/IPC boundary, controller-input capture contract, coexistence with the existing Steam QAM integration, and Full PID1902 lifecycle rules.  
> **Implementation state:** Design only. This document does **not** claim that the new Overlay process, Overlay transport, capture path, or Full PID1902 presentation integration is implemented or hardware-validated.  
> **Important:** Final WING/OEM1 button assignment remains intentionally deferred. The overlay architecture must not depend on which physical MSI button ultimately toggles it.

---

## 1. Design authorities

Read this document together with:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR2_5_MANDATORY_CONTROLLER_RUNTIME_LIFETIME_WORK_ORDER.md`
- `docs/work-order/PR3_REBOOT_BOUND_CONTROLLER_AUTHORITY_TRANSITION_WORK_ORDER.md`
- `docs/VIIPER_IMPLEMENTATION_RULES.md`
- `docs/VIIPER_INTEGRATION.md`
- `docs/VIIPER_MIGRATION_TODO.md`

Relevant current source seams include:

- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- `src/SteamInputAddonforClaw/Lifecycle/QamHostProcessController.cs`
- `src/SteamInputAddonforClaw.QamHost/Program.cs`
- `src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs`
- `src/SteamInputAddonforClaw.UI/App.xaml.cs`
- `src/SteamInputAddonforClaw.UI/SteamInputAddonforClaw.UI.csproj`
- `src/SteamInputAddonforClaw/Input/IControllerStateSnapshotSource.cs`
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs`
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckOutputStage.cs`

The project is pre-release. Obsolete Game Bar/X360 presentation experiments are not compatibility requirements.

---

## 2. Product goal

Provide a native Addon-owned handheld Quick Settings panel that can appear over a game without changing controller identity and without depending on Xbox Game Bar or Steam GamepadUI as its renderer.

Intended UX:

```text
Game / Windows desktop
        ↓
physical Quick Settings button
        ↓
Addon Quick Settings panel appears on the left
        ↓
controller navigation changes TDP / CPU Boost / FPS / Power Mode / future controls
        ↓
close panel
        ↓
return immediately to the same game and the same selected virtual presentation
```

The Addon Overlay is a **transient UI surface**. It is not:

- controller authority;
- a third virtual presentation;
- a PID mode;
- a DirectInput owner;
- a HidHide owner;
- a VIIPER owner;
- a replacement feature authority for TDP/CPU/FPS/etc.

Overlay visibility must never by itself change:

- Center M authority;
- PID1901/PID1902;
- DirectInput acquisition;
- HidHide configuration;
- VIIPER server/bus ownership;
- X360 vs SteamDeck selection.

---

## 3. Current preferred process architecture

```text
SteamInputAddonforClaw.exe
    Addon Runtime
    ├─ controller authority
    ├─ PID1902 / DirectInput
    ├─ ControllerState
    ├─ HidHide
    ├─ VIIPER presentation owner
    ├─ TDP / CPU / FPS / profile feature authorities
    ├─ overlay capture authority
    └─ UI process/IPC orchestration

SteamInputAddonforClaw.UI.exe
    existing WinUI settings frontend
    └─ ordinary disposable main UI

SteamInputAddonforClaw.QamHost.exe
    existing Steam GamepadUI/CEF integration
    └─ Steam QAM frontend only

SteamInputAddonforClaw.Overlay.exe
    new dedicated WinUI 3 process
    └─ Addon-owned native Quick Settings panel
```

The new Overlay should be a **separate executable/process** from both the Runtime and the existing main UI.

Do not turn `SteamInputAddonforClaw.UI.exe` into a persistent hidden multi-window host merely to add Quick Settings. The existing main UI currently has a clean lifecycle:

```text
MainWindow closes
→ frontend connection disposed
→ UI process exits
```

Preserve that lifecycle.

---

## 4. Why the Overlay should remain a separate process

### 4.1 Preserve the main UI lifecycle

The main settings application should not be rewritten around permanent background WinUI lifetime merely because a low-latency QAM surface is needed.

Preferred ownership:

```text
Runtime process
→ durable controller/device authority

Main UI process
→ normal settings UI lifetime

Overlay process
→ warm disposable Quick Settings presentation

Steam QamHost process
→ Steam-side QAM integration lifetime
```

### 4.2 Real failure isolation

Quick Settings will contain XAML controls, navigation logic, live snapshots, slider/toggle mutations, and future device controls.

A fatal Overlay XAML/dispatcher failure must not terminate:

- the controller Runtime;
- the main settings UI;
- the existing Steam QAM integration.

Desired boundary:

```text
Overlay.exe crashes
→ Runtime survives
→ current presentation ownership remains with Runtime
→ active Overlay capture is canceled safely
→ Main UI remains independently launchable
→ Steam QamHost remains independently managed
```

### 4.3 Independent iteration

The Overlay is expected to receive substantial layout/navigation tuning. Keeping it independent lets those changes evolve without continuously touching the main UI startup/close path.

### 4.4 Explicit cost

A warm second WinUI process consumes memory. That cost is accepted only if real MSI Claw measurements show acceptable hidden idle CPU, memory, and show latency.

Do not assume it is free; measure it.

---

## 5. Renderer and UI technology

### Preferred technology

Use:

```text
WinUI 3 XAML
+
ordinary top-level HWND
+
minimal Win32 window-style/position interop
```

Reasons:

- the repository already uses WinUI 3;
- the panel is ordinary controls, not an always-on performance HUD;
- the panel is opaque and rectangular;
- sliders/toggles/dropdowns/icons/scrolling/localization fit XAML naturally;
- no per-pixel transparent desktop surface is required.

### Explicitly out of scope initially

Do not add merely because this feature is called an overlay:

- WPF as a second UI framework;
- Direct2D custom controls;
- DirectComposition renderer architecture;
- DXGI/game injection;
- fullscreen transparent canvas;
- click-through region framework;
- hidden owner-window hierarchy;
- swappable `IOverlayHost` renderer abstraction;
- native rendering helper process;
- generalized HUD/compositor engine.

If real supported-game evidence proves ordinary WinUI HWND behavior inadequate, solve that proven deficiency later.

---

## 6. Visual/window contract

Target shape:

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
- rectangular window;
- top = monitor working-area top;
- bottom = monitor working-area bottom;
- taskbar is never intentionally covered;
- width determined later from actual icon/control layout;
- no transparency/acrylic/blur requirement.

Use the selected monitor's current work area rather than hard-coding taskbar dimensions:

```text
X      = WorkArea.Left
Y      = WorkArea.Top
Width  = OverlayPanelWidth
Height = WorkArea.Bottom - WorkArea.Top
```

Preferred monitor resolution on every Show:

1. monitor containing the current foreground game/window;
2. otherwise foreground/shell monitor;
3. otherwise primary monitor.

Do not build a persistent multi-monitor policy manager for unsupported edge cases.

Target top-level behavior:

```text
borderless
not user-resizable
not shown in taskbar
topmost
WS_EX_NOACTIVATE or equivalent
SWP_NOACTIVATE when shown/positioned
```

The game should remain foreground when the controller opens Quick Settings.

---

## 7. Physical input and controller navigation

Under Full PID1902 Addon authority:

```text
MSI Claw PID1902
      ↓
DirectInput
      ↓
ControllerState
      ↓
Addon Runtime
```

The Overlay process must **not**:

- open a second DirectInput session;
- read the virtual X360 controller through XInput;
- read the virtual SteamDeck controller back through GameInput/XInput;
- become controller-input authority.

Runtime already owns the canonical physical-input truth.

### No high-rate raw-state IPC

Do not stream the publisher's ~250 Hz `ControllerState` snapshots over JSON/named pipe.

Runtime should translate input into low-rate semantic events, for example:

```text
NavigateUp
NavigateDown
NavigateLeft
NavigateRight
AdjustLeft
AdjustRight
Accept
Back
PreviousSection
NextSection
CloseOverlay
```

Exact bindings are a UI decision. The architecture contract is semantic navigation, not raw reports.

### Logical selection

Because the window should not intentionally become foreground, controller navigation should support a logical selected item/highlight model rather than requiring normal activated keyboard focus.

---

## 8. Overlay capture is orthogonal to presentation selection

Full PID1902 presentation policy remains:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

Overlay policy is separate:

```text
Overlay hidden → current presentation live
Overlay active → same current presentation remains selected but game-facing input is neutral
```

Wrong:

```text
SteamDeck
→ Overlay opens
→ detach SteamDeck
→ attach X360
→ Overlay closes
→ detach X360
→ reattach SteamDeck
```

Correct:

```text
current presentation = X360 OR SteamDeck
→ Overlay opens
→ same presentation remains structurally selected/attached
→ current presentation neutral
→ controller navigates Overlay
→ Overlay closes
→ release gate
→ same presentation resumes live input
```

Overlay open/close must not mutate PID, DirectInput, HidHide, VIIPER server/bus, Steam/BPM state, or presentation selection.

---

## 9. Neutral-output contract

Initial policy: **full controller neutralization to the game while Overlay capture is active.**

Do not begin with per-button passthrough.

```text
Overlay hidden
→ physical ControllerState publishes normally

Overlay capture active
→ physical ControllerState feeds OverlayInputRouter
→ current selected virtual presentation remains attached
→ game-facing output remains neutral
```

The existing Steam Deck pause primitive demonstrates the desired ordering:

```text
stop publisher
→ write neutral
→ mark presentation paused
```

But the final Full PID1902 implementation must route this through the **one presentation owner** so the same overlay capture concept works for both X360 and SteamDeck.

Do not create a second presentation manager solely for Overlay.

---

## 10. Open contract

Avoid stranding game input neutral when no usable Overlay appeared.

Preferred sequence:

```text
physical ToggleAddonQuickSettings event
        ↓
Runtime verifies Overlay process/IPC ready
        ↓
retire another visible local control surface if required
        ↓
Runtime sends Show + fresh snapshot
        ↓
Overlay selects monitor/WorkArea and shows no-activate
        ↓
Overlay acknowledges Visible
        ↓
Runtime commits OverlayCapture
        ↓
current virtual presentation neutralizes
        ↓
semantic controller navigation begins
```

If Overlay start/connect/show fails:

```text
OverlayCapture does not commit
current presentation stays live
Runtime/controller continues normally
feature-local error is logged
```

Do not add an epoch/barrier framework for theoretical crossings.

---

## 11. Close and release-to-resume contract

Closing must not leak the close/navigation input into the game.

```text
Close requested
→ stop accepting Overlay navigation mutations
→ hide Overlay window
→ keep current presentation neutral
→ wait until controls consumed by Overlay are released/neutral
→ clear OverlayCapture
→ resume live publication on the SAME current presentation
```

This release gate is required because otherwise a held `B`, D-pad direction, or physical QAM trigger can immediately leak into the game when publication resumes.

Keep the solution narrow: held-control release, not a generalized synchronization framework.

---

## 12. Overlay process lifecycle

Preferred baseline: **warm hidden process**.

```text
Runtime ready
→ start Overlay.exe
→ initialize Windows App SDK/XAML
→ connect .Overlay endpoint
→ create window hidden
→ remain idle
```

Normal user path:

```text
button → Show
close  → Hide
button → Show
```

Do not cold-start Windows App SDK + XAML + process + pipe on every QAM button press unless measurements later prove that approach acceptable.

### Hidden-process crash

```text
Overlay.exe dies while hidden
→ controller unaffected
→ Steam QamHost unaffected
→ Main UI unaffected
→ next Overlay request may start a fresh process
```

No watchdog required initially.

### Active-process crash

```text
Overlay visible
+ current presentation neutral
+ Overlay.exe/pipe dies
→ Runtime cancels OverlayCapture
→ waits consumed controls release when relevant
→ resumes current presentation if underlying controller state is healthy
```

A real controller safety fault still wins; Overlay crash recovery must not override Full PID1902 fail-close.

---

## 13. IPC architecture — three dedicated endpoints

This is a frozen correction to the earlier draft.

### 13.1 Current implementation facts

Current Runtime already has separate desktop and Steam-QAM endpoints:

```text
CreateForCurrentUser()    → desktop frontend
CreateQamForCurrentUser() → existing Steam QamHost
```

`AddonProcessHost` owns separate `NamedPipeAddonFrontendServer` instances for them.

Each server currently accepts one connected frontend at a time. That is appropriate for its dedicated frontend.

### 13.2 Existing Steam QamHost continuously owns `.Qam` while connected

The existing `SteamInputAddonforClaw.QamHost.exe` creates `QamFrontendBridge`, calls `ConnectAsync()`, and keeps that bridge for the QamHost process lifetime.

Therefore:

> **QamHost process running/connected means `.Qam` remains occupied even when the Steam QAM panel is not visibly open.**

Steam-QAM panel visibility and QamHost pipe ownership are different facts.

Do not attempt to share the single-connection `.Qam` endpoint between the warm Overlay process and QamHost.

Do not stop/restart QamHost merely to lend its pipe to Overlay.

QamHost owns a Steam CDP/GamepadUI injection lifecycle; repeatedly tearing that down for every Addon Overlay toggle would be unnecessary coupling and failure surface.

### 13.3 Final endpoint split

Use:

```text
Runtime
├─ .Frontend → SteamInputAddonforClaw.UI.exe
├─ .Qam      → SteamInputAddonforClaw.QamHost.exe
└─ .Overlay  → SteamInputAddonforClaw.Overlay.exe
```

Add a narrow endpoint factory conceptually equivalent to:

```text
CreateOverlayForCurrentUser()
```

with the same CurrentUserOnly/local-user security boundary.

### 13.4 Separate pipe does not mean separate feature authority

All three frontends ultimately call the **same Runtime-owned feature instances**.

```text
Main UI ──────┐
Steam QAM ────┼──→ Runtime TDP / CPU / FPS / Profile / Power authorities
Addon Overlay ┘
```

There must not be:

- `OverlayTdpManager`;
- `QamTdpManager`;
- duplicate profile stores;
- duplicate EC helpers;
- duplicate hardware writers;
- frontend-owned persistence.

The processes and pipes are separate only for UI/process lifetime isolation.

### 13.5 Overlay transport should be narrow

Overlay needs:

- initial/fresh Quick Settings snapshots;
- mutations through existing Runtime authorities;
- authoritative mutation results;
- state invalidation/refresh;
- low-rate semantic navigation notifications;
- Show/Hide/Shutdown lifecycle messages or equivalent process control.

It does **not** need the entire developer/setup/diagnostics desktop frontend API.

A small Overlay-side typed wrapper is acceptable if it narrows the existing contract rather than duplicating feature logic.

Do not create a generalized event bus.

---

## 14. Steam QAM and Addon Overlay coexistence

The existing Steam QAM integration is **not removed by this architecture**.

The two processes may be alive simultaneously:

```text
QamHost.exe    connected to .Qam
Overlay.exe    connected to .Overlay
```

That is expected and safe because the endpoints are independent.

### 14.1 What must be mutually exclusive

The requirement is about **visible Quick Settings surfaces**, not process lifetime:

```text
Steam QAM panel visible
        XOR
Addon Overlay visible
```

Do not interpret this as:

```text
QamHost process running
        XOR
Overlay process running
```

Both helper/frontends may remain loaded/warm.

### 14.2 Never use process liveness as Steam-QAM visibility

QamHost remains connected while its injected QAM integration is loaded. Therefore:

```text
QamHost alive != Steam QAM panel visible
```

Any implementation that needs strict visual mutual exclusion must use a real Steam-QAM surface visibility/control signal, not `Process.HasExited` or pipe connection state.

The exact Steam-QAM visible/close seam should be validated against the current injected `qam.js`/GamepadUI behavior before its work order is frozen.

Do not invent a polling loop or generalized Steam UI state manager merely for this.

### 14.3 Opening Addon Overlay while Steam QAM is visibly open

Desired product behavior:

```text
Addon Overlay requested
→ retire/close visible Steam QAM surface through the narrow supported Steam-QAM seam
→ keep QamHost process and .Qam connection alive
→ show Addon Overlay
```

Do **not** terminate QamHost as the normal close mechanism.

### 14.4 Opening Steam QAM while Addon Overlay is visible

Desired behavior:

```text
Steam QAM requested
→ hide/retire Addon Overlay
→ complete Overlay release-to-resume
→ then allow/open Steam QAM
```

The exact physical-button policy remains deferred, but whichever route requests Steam QAM must not intentionally leave both surfaces visible.

### 14.5 Main UI coexistence

Main settings UI and Addon Overlay should also remain mutually exclusive visible surfaces:

```text
Main UI visible
        XOR
Addon Overlay visible
```

Main UI process lifetime should remain unchanged. If Overlay is requested while Main UI is open, use the Main UI's normal close path. If Main UI is requested while Overlay is open, retire Overlay capture first and then launch/show Main UI normally.

---

## 15. Settings/device-feature ownership

All UI surfaces are clients of Runtime authorities.

Examples:

```text
Overlay TDP control
→ Runtime TdpRuntime
→ existing hardware/persistence path
```

```text
Steam QAM CPU Boost
→ Runtime CpuBoostRuntime
```

```text
Main UI Power Mode
→ Runtime PowerModeRuntime
```

```text
Overlay FPS limit
→ Runtime Intel FPS/profile authority
```

Do not infer mutation success from local UI state. Runtime returns the authoritative result/readback.

---

## 16. Device vs active-game behavior

The current Steam QAM bridge distinguishes BPM/no-game device mutations from active-game profile mutations.

The new Overlay should preserve **product meaning**, not blindly copy the existing JavaScript bridge's UI.

Possible future UX:

```text
no active game
→ Device controls

active recognized game/profile
→ current-game profile controls
```

This specific presentation is not frozen here.

Frozen rule:

- active game/device facts come from Runtime;
- persistence belongs to Runtime;
- hardware mutation belongs to Runtime;
- Overlay does not create a second policy authority.

---

## 17. Game Bar policy

Xbox Game Bar is not the new Addon Quick Settings host.

Opening Addon Quick Settings must not trigger:

```text
Win+G
Game Bar foreground
GameBarForegroundWatcher presentation selection
SteamDeck → X360 presentation switch
```

The historical nested Game Bar/X360 presentation experiment remains historical.

Once a physical MSI button is assigned to Addon Quick Settings in Addon-owned mode, that button's native Game Bar action must not also fire.

Bad:

```text
physical QAM button
→ Addon Overlay
AND
→ Xbox Game Bar
```

Reuse the existing Win+G suppression foundation where applicable instead of adding another independent keyboard-hook authority.

Do not globally uninstall Xbox Game Bar or apply broad Windows policy simply to implement this feature.

Center M Enabled/stock-authority behavior remains a separate product-policy boundary and follows the final button/authority decision.

---

## 18. WING/OEM1 mapping remains deferred

Overlay architecture exposes one semantic operation:

```text
ToggleAddonQuickSettings()
```

Do not bake WING/OEM1 into Overlay IPC or capture logic.

Latest ergonomic candidate discussed, **not frozen**:

```text
Center M Disabled / Addon controller authority

                         Non-Steam / X360      Steam/BPM / SteamDeck
--------------------------------------------------------------------
WING (left)              user mapping           Steam Button
OEM1 (right)             Addon Overlay          Addon Overlay
Game Bar                 not invoked            not invoked
```

Reasons this candidate is attractive:

- Steam usage preserves left-side Steam-button semantics;
- the right hardware key behaves like a handheld Quick Access button;
- Addon Quick Settings is available in Steam and non-Steam contexts;
- Steam Button does not need to be sacrificed.

But this mapping can change without changing Overlay architecture.

Avoid delaying every QAM open behind a double-press timeout merely to fit two primary actions onto one button unless hardware/UX testing later justifies it.

---

## 19. Steam Button and Steam Quick Access remain valid capabilities

The custom Overlay does not require deleting existing SteamDeck synthetic system-button support:

```text
RequestSteamPulse()
RequestQuickAccessPulse()
```

The existing Steam QAM integration also remains supported by this architecture.

Even if final physical defaults stop mapping a dedicated button directly to Steam Quick Access, retain the primitive until a later focused cleanup proves it unused.

Do not couple Overlay visibility to SteamDeck system-button lifecycle.

---

## 20. Presentation change while Overlay is open

A normal real-world event may occur while Overlay is open: Steam/BPM state changes.

Rule:

> Overlay capture continues to own user input while visible; the Full PID1902 presentation owner may reconcile its desired X360/SteamDeck selection, but the newly selected presentation remains neutral until Overlay capture ends.

Example:

```text
Overlay open while X360 selected
→ Steam game becomes active
→ Full PID1902 owner selects SteamDeck normally
→ SteamDeck remains neutral because OverlayCapture is active
→ Overlay closes
→ release gate completes
→ SteamDeck becomes live
```

Do not create `SteamDeckOverlayMode` / `XboxOverlayMode` combined states.

---

## 21. Physical input loss while Overlay is open

If PID1902/DirectInput disappears:

```text
Runtime detects physical loss
→ existing Full PID1902 reconcile/fail-close owns recovery
→ virtual output remains safe/neutral
→ Overlay does not attempt reacquisition
```

Initial implementation may close Overlay after a real physical-input failure if that provides the simplest reliable UX.

Do not add Overlay-specific PnP recovery machinery.

---

## 22. Sleep / hibernate / resume

Quick Settings is not an authority boundary.

Before suspend:

```text
if Overlay visible
→ hide/retire Overlay interaction
→ controller suspend lifecycle continues under Full PID1902 owner
```

After resume:

```text
Runtime reconciles controller authority/PID/DirectInput/HidHide/VIIPER first
→ Overlay process may reconnect/recreate if necessary
→ Overlay remains hidden
```

Do not automatically reopen stale Quick Settings after resume.

No Overlay-specific resume state machine is needed.

---

## 23. Runtime restart/shutdown

Under Center M Disabled, Runtime is the mandatory controller authority.

Overlay and QamHost are subordinate UI/helper processes.

Controlled Runtime restart:

```text
retire active Overlay capture
→ Overlay/QamHost lose or close their Runtime IPC as appropriate
→ Runtime performs normal controller teardown/restart contract
→ new Runtime starts
→ UI helper processes reconnect/restart according to their own lifecycle
```

Overlay must not attempt to preserve controller ownership across Runtime restart.

On Runtime shutdown, failure to close a disposable Overlay process must never become controller teardown authority.

---

## 24. Performance/POC requirements

Measure on supported MSI Claw hardware:

```text
Overlay.exe hidden idle CPU
Overlay.exe hidden Working Set / Private Bytes
first initialization cost
warm hidden → first visible frame latency
Show → Hide → Show latency
game foreground before/after Show
frametime/VRR impact while hidden
frametime/VRR impact while visible
```

Hidden state should have:

- no controller polling loop in Overlay;
- no animation loop;
- no high-rate telemetry loop unless a visible control explicitly needs it;
- negligible CPU activity.

Visible state should use normal WinUI rendering and low-rate snapshot/notification updates.

Cold-starting the entire process on every button press is not the preferred default.

---

## 25. Fullscreen compatibility target

Initial target:

```text
Windowed games                      required
Borderless fullscreen               required
modern flip/fullscreen-optimized    hardware validation required
true legacy exclusive fullscreen    best effort / not guaranteed initially
```

Do not add graphics injection or a custom swap-chain overlay to defend against rare/unsupported fullscreen modes without actual product evidence.

---

## 26. Packaging

The separate executable is an internal product component, not a second installer.

Goals:

- one installer;
- Runtime remains the main platform process;
- existing main UI packaging remains stable;
- existing Steam QamHost remains packaged for Steam QAM;
- new Overlay.exe is packaged as another internal component;
- avoid accidentally duplicating unnecessary Windows App SDK payloads.

Inspect actual publish/package output before freezing deployment layout.

---

## 27. Security and product scope

All UI endpoints remain local/current-user only.

For `.Overlay`, preserve the same `PipeOptions.CurrentUserOnly` boundary used by existing frontend transport.

Supported product scope remains:

```text
1 Windows user
1 interactive session
```

Do not introduce Fast User Switching/RDP/multi-session arbitration, service brokers, or cross-user UI authority.

---

## 28. Logging

Useful low-rate lifecycle events:

```text
Overlay process start/exit
.Overlay IPC connected/disconnected
Show requested / Visible confirmed
Hide requested / Hidden confirmed
OverlayCapture entered/exited
capture canceled due to Overlay disconnect
Steam QAM surface → Addon Overlay handoff
Addon Overlay → Steam QAM surface handoff
Main UI ↔ Overlay handoff
selected WorkArea summary
Runtime mutation failure
```

Do not log raw controller state or every navigation event at Info level.

---

## 29. State refresh model

Preferred model:

```text
Overlay Show
→ one fresh aggregate Quick Settings snapshot

Runtime feature change
→ low-rate StateInvalidated / targeted notification
→ refresh relevant values

user mutation
→ Runtime authoritative result/readback
```

Do not create a constant high-frequency polling loop merely because Overlay is visible.

This is Quick Settings, not ClawHUD telemetry.

---

## 30. First feature scope

Reasonable first controls, because Runtime already owns them:

- TDP;
- CPU Boost;
- Windows Power Mode;
- Intel FPS Limit;
- current game profile enable/state where useful;
- fan/fan curve after its production runtime contract exists;
- resolution/refresh only after in-game mutation policy is proven safe.

Do not block the window/input POC on complete feature-card design.

---

## 31. Recommended implementation sequence

### OQ1 — Window/process POC

Create a minimal `SteamInputAddonforClaw.Overlay.exe`:

- WinUI 3;
- one opaque left panel;
- WorkArea positioning;
- topmost/no-activate behavior;
- Show/Hide;
- hidden idle mode.

Validate focus, taskbar avoidance, DPI, representative games, memory, CPU, and latency.

No controller neutralization yet.

### OQ2 — Dedicated `.Overlay` transport and warm lifecycle

- add `CreateOverlayForCurrentUser()` or equivalent;
- add a dedicated Overlay named-pipe server/client;
- keep `.Frontend` unchanged;
- keep `.Qam` unchanged and owned by existing Steam QamHost;
- warm-start Overlay hidden;
- make Overlay disconnect feature-local;
- do not make existing transport multi-client.

### OQ3 — Visible-surface coexistence

- preserve QamHost process and `.Qam` connection while Addon Overlay is used;
- identify/validate a narrow real Steam-QAM visible/close/open seam;
- prevent Steam QAM panel and Addon Overlay from being intentionally visible simultaneously;
- keep Main UI and Addon Overlay mutually exclusive;
- do not use process liveness as Steam-QAM visibility.

### OQ4 — Overlay capture / neutral publication

- Runtime owns `OverlayCapture`;
- pause/neutral current presentation through the one Full PID1902 presentation owner;
- semantic controller commands only;
- release-to-resume latch;
- Overlay crash/disconnect cancels capture;
- Overlay open/close never switches X360/SteamDeck.

### OQ5 — Actual Quick Settings controls

Add a small first set through existing Runtime feature authorities. No duplicate feature managers.

### OQ6 — Physical button / Game Bar policy

After Overlay itself is proven:

- bind final WING/OEM1 policy to `ToggleAddonQuickSettings()`;
- ensure selected Addon QAM button does not also invoke Game Bar;
- preserve Steam Button according to final mapping decision;
- decide whether/how direct Steam Quick Access remains mapped.

### OQ7 — Hardware lifecycle validation

Validate:

- rapid repeated Show/Hide;
- release-to-resume leakage;
- Steam/BPM transition while Overlay open;
- Overlay crash hidden and visible;
- QamHost remains stable while Overlay is repeatedly used;
- switching visible surface Steam QAM ↔ Addon Overlay;
- sleep/hibernate/resume;
- Runtime controlled restart;
- physical input loss/PnP re-enumeration;
- presentation failure.

---

## 32. Relationship to Full PID1902 roadmap

Overlay UI work must not derail the controller-authority sequence.

Full PID1902 remains responsible for:

```text
persistent HidHide
mandatory Runtime lifetime
reboot-bound authority transition
Disabled-boot admission
PID1902 / DirectInput ownership
first presentation
X360 ↔ SteamDeck selection
real lifecycle recovery
```

The WinUI shell/IPC POC can happen independently.

Production capture/neutralization must integrate with the **final Full PID1902 presentation owner**, not harden dependencies on obsolete route-scoped Game Bar/X360 logic.

---

## 33. Anti-overengineering rules

Do not introduce without concrete product need:

- `OverlayAuthorityManager`;
- generalized UI-surface arbitration framework;
- render-host hierarchy;
- overlay epoch/barrier system;
- heartbeat supervisor;
- service/process watchdog;
- multi-user session broker;
- duplicate TDP/profile authority;
- controller-state message bus;
- multi-client rewrite of the existing `.Qam` or `.Frontend` pipes.

Protect realistic failures:

- Overlay process dies while controller output is neutral;
- Overlay cannot show after request;
- Steam QAM/Overlay visual handoff fails;
- Main UI/Overlay handoff fails;
- Runtime restart/shutdown;
- sleep/hibernate/resume;
- physical device loss;
- presentation failure;
- real setting mutation failure.

Keep one controller owner, one presentation owner, and one Runtime feature authority per feature.

---

## 34. POC acceptance checklist

### Window

- [ ] correct display selected;
- [ ] left aligned;
- [ ] WorkArea height used;
- [ ] taskbar not covered;
- [ ] game foreground not intentionally stolen;
- [ ] visible over representative borderless/fullscreen-optimized games;
- [ ] repeated Show/Hide clean.

### Performance

- [ ] hidden CPU effectively idle;
- [ ] hidden memory recorded;
- [ ] warm Show latency recorded;
- [ ] visible frametime impact checked;
- [ ] hidden frametime/VRR impact checked;
- [ ] no raw high-rate controller IPC.

### IPC coexistence

- [ ] Main UI can own `.Frontend` while QamHost owns `.Qam`;
- [ ] Overlay can own `.Overlay` at the same time;
- [ ] QamHost does not need to disconnect for Overlay to work;
- [ ] Overlay does not connect to `.Qam`;
- [ ] QamHost remains connected while its Steam QAM panel is merely hidden;
- [ ] process liveness is not treated as QAM visibility.

### Controller capture

- [ ] current presentation becomes neutral while Addon Overlay active;
- [ ] D-pad/stick navigation does not also move the game;
- [ ] Accept/Back do not leak;
- [ ] close trigger does not leak after resume;
- [ ] same presentation remains selected before/after Overlay;
- [ ] no PID/HidHide/DirectInput churn.

### Visible-surface coexistence

- [ ] Addon Overlay request can retire a visible Steam QAM surface without killing QamHost;
- [ ] Steam QAM request retires Addon Overlay first;
- [ ] Main UI and Addon Overlay are not intentionally visible together;
- [ ] failed handoff leaves controller usable.

### Failure/lifecycle

- [ ] killing Overlay hidden does not affect Runtime/QamHost;
- [ ] killing Overlay visible safely releases capture when controller state is otherwise healthy;
- [ ] failed Overlay startup leaves controller live;
- [ ] Runtime shutdown does not hang on Overlay;
- [ ] suspend/resume does not reopen stale Overlay;
- [ ] physical controller loss uses existing Full PID1902 fail-close/reconcile.

---

## 35. Current decisions vs deferred decisions

### Current design baseline

1. Quick Settings is Addon-owned, not Game Bar-hosted.
2. Add `SteamInputAddonforClaw.Overlay.exe` as a dedicated WinUI 3 process.
3. Main `SteamInputAddonforClaw.UI.exe` retains its existing lifecycle.
4. Existing `SteamInputAddonforClaw.QamHost.exe` and Steam QAM remain supported.
5. Use **three dedicated UI endpoints**: `.Frontend`, `.Qam`, `.Overlay`.
6. Existing `.Qam` remains exclusively for QamHost; Overlay does not borrow or replace it.
7. QamHost and Overlay may both remain alive/connected simultaneously.
8. Steam QAM panel and Addon Overlay must not be intentionally visible simultaneously.
9. QamHost process/pipe liveness is not Steam-QAM panel visibility.
10. Runtime remains the only controller/device/settings authority.
11. Runtime owns physical `ControllerState`.
12. Overlay receives semantic controller navigation, not raw high-rate reports.
13. Overlay open/close never selects X360 vs SteamDeck.
14. Current selected presentation is neutral while Overlay captures controller navigation.
15. Release-to-resume prevents held input from leaking back into the game.
16. Overlay process/IPC loss while active cancels capture safely.
17. Panel is opaque, rectangular, left-side, and uses monitor WorkArea height.
18. Use minimal Win32 HWND interop; do not build a custom compositor/HUD renderer initially.
19. Game Bar foreground is not a presentation-selection event.
20. The final physical button assigned to Addon Quick Settings must not simultaneously invoke Game Bar.

### Deferred product decisions

- final WING vs OEM1 assignment;
- final non-Steam WING user-mapping catalog;
- whether Steam Quick Access remains directly user-mappable after Addon Overlay ships;
- exact Steam-QAM panel visibility/close/open integration seam;
- exact first-page card order;
- exact panel width;
- exact logical focus visuals;
- touch/mouse priority;
- exact physical-input-failure Overlay UX;
- cleanup timing for obsolete historical Game Bar/X360 presentation code.

These may change without redesigning the process/controller architecture.

---

## 36. Final target architecture

```text
                         FULL PID1902 ADDON RUNTIME
                                  │
        ┌─────────────────────────┼─────────────────────────┐
        │                         │                         │
 Controller authority       Feature authorities       UI orchestration
 PID1902 / DirectInput      TDP / CPU / FPS / etc.   Overlay capture
        │                         │                         │
        └──────────────┬──────────┘                         │
                       │                                    │
                 ControllerState                            │
                       │                                    │
        ┌──────────────┴──────────────┐                     │
        │                             │                     │
 Presentation owner             OverlayInputRouter          │
 X360 OR SteamDeck              semantic commands          │
        │                             │                     │
 live when Overlay hidden             └──────────┬──────────┘
 neutral when Overlay active                     │
                                                 │
                    ┌────────────────────────────┼────────────────────────────┐
                    │                            │                            │
                .Frontend                      .Qam                       .Overlay
                    │                            │                            │
                    ▼                            ▼                            ▼
        SteamInputAddonforClaw.UI.exe   SteamInputAddonforClaw.QamHost.exe  SteamInputAddonforClaw.Overlay.exe
        normal settings frontend        Steam GamepadUI/CEF QAM             WinUI native Quick Settings
```

Process/IPC coexistence:

```text
UI.exe      may own .Frontend
QamHost.exe may own .Qam
Overlay.exe may own .Overlay

all simultaneously
```

Visible-surface policy:

```text
Steam QAM visible XOR Addon Overlay visible
Main UI visible   XOR Addon Overlay visible
```

Controller presentation remains independent:

```text
Steam/BPM inactive → X360
Steam/BPM active   → SteamDeck
```

Overlay remains orthogonal:

```text
Overlay hidden → selected presentation live
Overlay shown  → selected presentation neutral + controller navigates Addon Quick Settings
```

---

## 37. Final design principles

1. **Quick Settings is a UI surface, not a controller presentation.**
2. **Opening Addon Overlay must never switch X360 ↔ SteamDeck merely because it is visible.**
3. **Runtime keeps all controller/device/settings authority.**
4. **Overlay.exe owns only native WinUI presentation and transient logical selection.**
5. **Steam QamHost remains a separate supported frontend and keeps its own `.Qam` connection.**
6. **Overlay gets its own `.Overlay` endpoint; never borrow `.Qam`.**
7. **QamHost and Overlay may be loaded simultaneously; only their visible Quick Settings surfaces are mutually exclusive.**
8. **Do not kill/restart QamHost as the normal way to show Addon Overlay.**
9. **Do not use QamHost process/pipe liveness as Steam-QAM visibility.**
10. **Do not stream high-rate ControllerState to any UI process.**
11. **Neutralize the currently selected presentation while Addon Overlay owns controller navigation.**
12. **Release Overlay-consumed controls before returning live input to the game.**
13. **Overlay crash must not strand a healthy controller in permanent neutral state.**
14. **Main UI lifecycle stays unchanged.**
15. **Use a normal opaque WinUI HWND; do not build a compositor/HUD engine without evidence.**
16. **Use monitor WorkArea so the taskbar is not intentionally covered.**
17. **Game Bar is not the Addon's Quick Settings host and not a presentation-selection event.**
18. **Physical WING/OEM1 assignment remains a replaceable product policy above this architecture.**
19. **Measure real hidden memory, idle CPU, show latency, focus behavior, and game compatibility before calling the renderer choice final.**
20. **Protect realistic handheld lifecycle failures without adding theoretical-race machinery.**
21. **Prefer one controller owner, one presentation owner, one Runtime feature authority per feature, and separate disposable UI clients.**
