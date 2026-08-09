# Steam Input Addon for Claw

> [!WARNING]
> This project is under active development and is not functional yet. Do not install or use it yet.

A lightweight Steam Input bridge for MSI Claw handheld PCs.

The project exposes the MSI Claw built-in controller to Steam as a **Classic Steam Controller**, allowing the rear M1/M2 buttons to appear as independent Steam Controller grip buttons.

The addon intentionally does not implement its own remapping, macros, profiles, or controller configuration system. Those functions are delegated to **Steam Input**.

> Unofficial project. Not affiliated with MSI or Valve.

## Project

* **Platform:** Windows 11 24H2 or later
* **Device family:** MSI Claw handheld PCs
* **Architecture:** x64
* **Application:** WinUI 3 / .NET 10
* **Distribution:** Velopack
* **License:** GPL-3.0-or-later

---

# Goals

The addon exists for one specific purpose:

```text
MSI Claw built-in controller
        ↓
Steam Input Addon for Claw
        ↓
Classic Steam Controller
        ↓
Steam
        ↓
Steam Input
        ↓
Game
```

For games launched through Steam, including **Non-Steam Shortcuts**, the Claw built-in controller should be exposed as a Classic Steam Controller.

Fixed rear-button mapping:

```text
Claw M1 → Steam Controller Left Grip
Claw M2 → Steam Controller Right Grip
```

Steam Input is responsible for everything that happens after this physical mapping.

This includes:

* controller remapping;
* keyboard and mouse mapping;
* macros;
* turbo;
* long press;
* double press;
* Action Sets;
* Action Layers;
* radial menus;
* per-game layouts;
* other Steam Input features.

The addon must not duplicate these features.

---

# Non-Goals

This project is **not** intended to become:

* a general controller manager;
* a controller remapping application;
* a macro editor;
* a keyboard/mouse mapping application;
* a game profile manager;
* a game database;
* a Steam Input replacement;
* a Handheld Companion replacement;
* a ClawTweaks replacement.

The addon should remain a small routing layer between the MSI Claw controller and Steam Input.

---

# Supported Environments

The target routing environments are:

1. **Stock MSI Center M**
2. **MSI Center M + ClawTweaks**

ClawTweaks support is compatibility behavior only.

ClawTweaks is **not a runtime dependency** and the addon must not require modifications to ClawTweaks.

The addon must also preserve unrelated ClawTweaks features such as:

* TDP controls;
* fan controls;
* OSD;
* performance controls;
* other non-controller functionality.

**Handheld Companion (HHC) is not a third addon routing mode.** When HHC controller management is actively managing the Claw controller, this addon remains completely passive because HHC already owns controller virtualization/routing. Merely having HHC installed is not a veto; an installed but inactive HHC environment does not block the normal Stock Center M or ClawTweaks paths.

---

# Core Behavioral Rule

The addon may intervene only when:

```text
Steam session active
AND
no external physical controller
AND
HHC controller management is not active
```

Anything else means:

```text
PASSIVE
```

PASSIVE should behave as closely as possible to the addon not being installed.

In PASSIVE state:

* no addon virtual controller exists;
* the MSI internal controller is not unnecessarily acquired;
* the MSI controller mode is not changed;
* HidHide is not modified by the addon;
* normal MSI Center M behavior remains available;
* normal ClawTweaks controller behavior remains available;
* normal HHC controller behavior remains available.

The addon should avoid persistent system-wide controller modifications whenever possible.

---

# State Priority

External controller detection has the highest priority.

Conceptually:

```text
External physical controller present?
    YES
    → PASSIVE / VETO

    NO
    ↓

HHC controller management active?
    YES
    → PASSIVE / HHC-MANAGED

    NO
    ↓

Steam session active?
    NO
    → PASSIVE

    YES
    ↓

Steam Controller routing active
```

During Steam routing:

```text
Xbox Game Bar foreground?
    YES
    → Steam Controller stays connected but neutral
    → temporary Xbox 360 output receives live input

    NO
    → temporary Xbox 360 output off
    → Steam Controller receives live input
```

External-controller veto always overrides every other state. An active HHC controller-management environment also prevents addon routing, but it is classified separately from an external physical-controller veto.

---

# Steam Session Detection

Steam session lifetime is determined from:

```text
HKCU\Software\Valve\Steam
RunningAppID
```

Interpretation:

```text
RunningAppID == 0
→ Steam session inactive

RunningAppID != 0
→ Steam session active
```

This state is used for both:

* normal Steam games;
* Non-Steam Shortcuts launched through Steam.

The addon must not use foreground process identity as the Steam-session lifetime.

For example:

```text
Game
→ Alt-Tab
→ Explorer
→ Discord
→ Browser
→ Steam
→ Game
```

must remain one continuous Steam routing session.

The Steam override ends only when `RunningAppID` returns to `0`, unless an external-controller veto or another higher-priority pass-through condition occurs first.

Registry monitoring should be event-driven where practical.

---

# External Controller Veto

If any external physical game controller is present, the addon must remain completely passive.

Examples include:

* Xbox controllers;
* DualSense;
* DualShock;
* 8BitDo controllers;
* other external USB/Bluetooth gamepads.

The addon must not acquire those controllers or alter their behavior.

External-controller detection should use Windows device information rather than XInput slot counting.

Preferred basis:

* PnP;
* SetupAPI;
* device instance identity;
* physical device/container identity;
* `DEVPKEY_Device_ContainerId` or equivalent container-level information where useful.

XInput slot occupancy alone must not be treated as authoritative physical-controller detection.

## Devices excluded from the veto

The following must not be mistaken for external physical controllers:

* MSI Claw internal controller interfaces;
* addon-owned VIIPER devices;
* ClawTweaks-owned virtual controllers;
* Handheld Companion virtual controllers;
* USB/IP virtual devices;
* ViGEm virtual devices.

This distinction is critical because a virtual controller may appear in Windows as a normal USB controller.

HHC virtual outputs are excluded from the **external physical-controller** detector even though active HHC controller management independently causes the addon to remain passive.

---

# Addon-Owned Virtual Device Tracking

VIIPER uses USB/IP and may expose devices that resemble physical USB devices to Windows.

Therefore the addon must **not rely only on a generic “virtual device” flag**.

Addon-created virtual controllers should be explicitly tracked.

Preferred strategy:

```text
Before virtual-device creation
→ snapshot relevant controller/device identities

Create VIIPER device

Wait for enumeration

After creation
→ compare device state
→ identify newly created addon-owned device
→ record its path / instance / container identity
```

Tracked addon-owned virtual devices are always excluded from external-controller veto detection.

Useful identities may include:

* device path;
* PnP instance ID;
* container ID;
* VID/PID;
* parent/child device relationships.

VID/PID alone should not be considered sufficient identity.

---

# External Controller Hotplug

If an external physical controller appears while Steam routing is active:

```text
1. Stop addon routing
2. Remove addon virtual outputs
3. Restore native MSI/ClawTweaks state
4. Set ExternalControllerVeto
```

The veto remains latched until the current Steam session ends.

Example:

```text
Steam session starts
→ addon active

Xbox controller connected
→ addon disengages

Xbox controller disconnected
→ addon remains passive

RunningAppID becomes 0
→ veto cleared

Next Steam session
→ normal eligibility evaluation again
```

This avoids repeated virtual-controller hotplug and Steam Input device rebinding during one game session.

---

# MSI Claw Physical Input

The preferred physical input source is **DirectInput**.

Known MSI controller interfaces:

```text
VID 0x0DB0

PID 0x1901 → XInput
PID 0x1902 → DirectInput
PID 0x1903 → testing / other mode
```

Known rear-button mapping:

```text
M1 → DirectInput Buttons[15]
M2 → DirectInput Buttons[16]
```

M1 and M2 must be treated as independent physical inputs.

They must not be reconstructed from the XInput interface because XInput cannot expose both rear buttons independently.

---

# Normalized Controller State

Physical input should be translated into an addon-owned normalized state before being passed to virtual outputs.

Conceptually:

```text
ControllerState
```

should contain at least:

```text
A
B
X
Y

D-Pad Up
D-Pad Down
D-Pad Left
D-Pad Right

LB
RB
LT
RT

Left Stick X/Y
Right Stick X/Y

L3
R3

Start
Select

M1
M2
```

Physical-device reading and virtual-controller report formatting should remain separate responsibilities.

This allows the input source and virtual output implementations to evolve independently.

---

# Stock MSI Center M Behavior

When the addon is passive, MSI Center M and the internal controller must remain untouched.

When a Steam session becomes eligible:

```text
1. Snapshot current native/controller state
2. Switch to DirectInput only if required
3. Wait for PID_1902
4. Acquire only the internal MSI Claw DirectInput device
5. Apply only hiding necessary for routing
6. Create the virtual Steam Controller
7. Begin routing
```

On exit:

```text
1. Remove temporary Xbox 360 output if present
2. Remove addon Steam Controller
3. Release DirectInput
4. Restore addon-owned HidHide changes
5. Restore the exact previous MSI controller state
```

The addon must never assume that the original controller mode was XInput.

The original state must be observed and restored.

---

# ClawTweaks Compatibility

ClawTweaks may already perform:

* controller virtualization;
* DirectInput handling;
* HidHide configuration;
* USB/IP / VIIPER output.

The addon must coexist with that environment without relying on private ClawTweaks internals.

Rules:

* do not require ClawTweaks modification;
* do not require private ClawTweaks IPC;
* do not steal a ClawTweaks-owned virtual controller;
* do not mutate a ClawTweaks-owned virtual controller;
* do not assume ViGEmBus exists;
* do not assume a standalone `viiper.exe` exists.

During addon Steam routing:

```text
Claw physical input
→ addon
→ Steam Controller
→ Steam Input
```

ClawTweaks button mappings/macros should not also reach the game.

Duplicate controller output must be prevented.

When addon routing ends, normal ClawTweaks controller behavior must be restored.

---

# Handheld Companion Coexistence

Handheld Companion (HHC) is treated as an **owner/veto environment**, not as another compatibility-routing mode.

When HHC controller management is active:

```text
HHC manages the controller
→ addon PASSIVE
```

The addon must not compete with HHC for controller ownership or create a second virtual output.

In an HHC-managed state:

* do not acquire the MSI Claw controller;
* do not change MSI controller mode;
* do not change HidHide configuration;
* do not create addon VIIPER devices;
* do not create an addon Steam Controller;
* do not mutate or steal HHC-owned virtual devices.

HHC being **installed but inactive** is not sufficient to veto addon routing. Detection should determine whether HHC controller management is actually active using public OS-visible evidence where practical, such as process/device/topology identity. The addon must not depend on private HHC IPC.

HHC-owned virtual controllers must remain excluded from external physical-controller detection.

If HHC controller management becomes active while addon Steam routing is already active:

```text
1. Disengage addon routing
2. Remove addon virtual outputs
3. Restore native state and addon-owned HidHide changes
4. Latch HHC-managed veto for the current Steam session
```

If HHC is subsequently stopped during the same Steam session, the addon must **not** reactivate until `RunningAppID` returns to `0`. This avoids controller ownership oscillation and Steam Input hotplug/rebinding during a game session.

---

# Virtual Output

The primary v1 output is:

```text
Classic Steam Controller

VID: 0x28DE
PID: 0x1102
```

The newer Steam Controller 2026 / Triton protocol is intentionally not the initial target.

The Classic Steam Controller is preferred because its two grip inputs map naturally to:

```text
M1 → Left Grip
M2 → Right Grip
```

The addon should use VIIPER / usbip-win2 for virtual output.

Preferred implementation:

```text
embedded libVIIPER.dll
```

Do not make the following hard requirements:

* ViGEmBus;
* standalone `viiper.exe`.

---

# Virtual Output Architecture

The addon may need two independent virtual devices:

```text
VirtualOutputManager
│
├─ SteamControllerOutput
│    └─ persistent for the eligible Steam session
│
└─ Xbox360CompanionOutput
     └─ temporary while Xbox Game Bar is foreground
```

The Steam Controller and Xbox 360 output must have independent lifecycles.

The implementation should not assume a single active virtual target.

---

# Steam Controller Lifetime

Once created for an eligible Steam session, the Classic Steam Controller should remain enumerated until:

* the Steam session ends;
* an external-controller veto forces complete disengagement; or
* HHC controller management becomes active and forces pass-through.

Normal foreground changes must not recreate it.

Xbox Game Bar must not recreate it.

The goal is to minimize:

* Steam Input hotplug events;
* configuration rebinding;
* player-slot changes;
* device-lost events.

---

# Xbox Game Bar Routing

Xbox Game Bar and controller-oriented ClawTweaks UI navigation require an XInput-compatible controller.

However, persistent Steam Controller hotplug should be avoided.

Therefore Game Bar uses a special routing mode.

## Game Bar foreground

```text
Classic Steam Controller
→ stays enumerated
→ receives neutral reports

Temporary Xbox 360 controller
→ enabled/created
→ receives live Claw input
```

The Steam game therefore receives no live controller input while the user navigates Game Bar.

## Game Bar exit

```text
Temporary Xbox 360 controller
→ removed/disabled

Same Classic Steam Controller
→ live reports resume
```

The Steam Controller must not be disconnected and recreated during this transition.

---

# Game Bar Detection

The addon should detect Xbox Game Bar independently of ClawTweaks.

Preferred mechanism:

```text
SetWinEventHook(EVENT_SYSTEM_FOREGROUND)
```

Then:

```text
foreground HWND
→ owning PID
→ process/package identity
→ Game Bar classification
```

Only Xbox Game Bar causes the special XInput routing state.

Ordinary foreground applications such as Explorer, browsers, Discord, Steam Client, etc. must not affect controller routing.

---

# HidHide Coordination

HidHide may already be configured by another application.

The addon must never assume exclusive ownership of its configuration.

Rules:

* read existing state before changing anything;
* add only entries required by this addon;
* record exactly what the addon added or changed;
* remove only addon-owned changes;
* preserve unrelated application/device entries;
* whitelist the addon executable when required to access a hidden MSI controller;
* preserve ClawTweaks/HHC HidHide configuration.

Replacing the complete HidHide configuration is forbidden.

HidHide is a required prerequisite for supported addon routing. The installer provides it for Stock MSI Center M; in a ClawTweaks environment the addon reuses the existing compatible installation. A missing HidHide installation after setup is a broken prerequisite: the addon remains PASSIVE and performs no routing mutation.

The v1 compatibility baseline is the official HidHide 1.5.230 release. The addon uses its persistent configuration API with recovery journaling; newer process/session blacklist APIs are not required by v1.

Installing HidHide alone must not hide the MSI Claw controller. While the addon is PASSIVE, it owns no HidHide device hiding or whitelist lease. In Stock MSI Center M environments the physical controller remains normally exposed; existing ClawTweaks/HHC HidHide configuration and controller exposure state are left unchanged.

Where a supported HidHide version provides process/session-scoped hiding, it may be used as an additional safety mechanism.

It must not be assumed to exist on every installed HidHide version.

---

# Recovery

All native-state changes must be reversible.

Before entering controller override mode, persist a recovery journal.

The journal should contain enough information to restore at least:

```text
override active state
original MSI controller mode/state
HidHide changes made by this addon
addon virtual outputs created
other addon-owned routing changes
```

On clean exit:

```text
restore native state
→ remove addon changes
→ clear recovery journal
```

On application startup:

```text
recovery journal incomplete?
    YES
    → recover before normal routing/UI

    NO
    → continue normally
```

Crash recovery takes priority over normal controller initialization.

The addon must not leave the internal controller:

* hidden;
* stuck in DirectInput mode;
* unavailable to MSI Center M;
* unavailable to ClawTweaks;
* unavailable to HHC

after a crash.

---

# Dependency Philosophy

Possible routing dependencies include:

```text
libVIIPER
usbip-win2
HidHide
DirectInput support/library
```

ViGEmBus is not a required dependency.

ClawTweaks systems may already provide some compatible components, but the addon must not rely on ClawTweaks being installed.

Dependency detection should distinguish between:

* installed;
* usable;
* missing;
* incompatible;
* repair/reboot required

where relevant.

Installed drivers must not alter normal controller behavior while the addon is passive.

---

# Architecture

Expected high-level components:

```text
SteamInputAddonforClaw
│
├─ SteamSessionWatcher
│    └─ RunningAppID monitoring
│
├─ ExternalControllerDetector
│    └─ PnP / SetupAPI physical-controller veto
│
├─ MsiClawInputSource
│    └─ PID_1902 DirectInput
│
├─ MsiControllerModeManager
│
├─ ControllerState
│
├─ VirtualOutputManager
│    ├─ SteamControllerOutput
│    └─ Xbox360CompanionOutput
│
├─ GameBarDetector
│
├─ HidHideCoordinator
│
├─ EnvironmentDetector
│    ├─ Stock Center M
│    ├─ ClawTweaks-compatible environment
│    └─ HHC-managed pass-through environment
│
└─ RecoveryManager
```

Component boundaries should remain narrow.

Controller reading, state normalization, environment modification, virtual output, Steam-session detection and recovery should not be unnecessarily coupled.

Device-specific hardware handling is isolated behind a handheld-device adapter boundary. Normalized controller state supports a variable number of device-specific auxiliary controls; their physical names and count must not be hard-coded into the routing core. MSI Claw is the first supported device implementation.

MSI Claw controller identities and auxiliary-control definitions belong to its device module. Internal-controller classification is delegated to the active device-specific matcher rather than the routing core.

---

# Primary State Model

Conceptually:

```text
                 ┌─────────────────────────────┐
                 │ External controller present │
                 └──────────────┬──────────────┘
                                │ YES
                                ▼
                         PASSIVE / VETO
                                │
                                │ until Steam session ends
                                │
                                ▼

External controller absent
        │
        ▼
HHC controller management active?
        │
        ├─ YES → PASSIVE / HHC-MANAGED
        │
        └─ NO
             ▼
RunningAppID == 0
        │
        ├─ YES → PASSIVE
        │
        └─ NO
             ▼
      Steam override active
             │
             ▼
   Game Bar foreground?
        │
        ├─ NO
        │    Steam Controller = LIVE
        │    Xbox360 = OFF
        │
        └─ YES
             Steam Controller = NEUTRAL
             Xbox360 = LIVE
```

---

# Safety Invariants

The following are architectural invariants.

## Passive invariant

When intervention is unnecessary, the machine should behave as though the addon were not installed.

## External-controller invariant

The addon never takes control while a separate physical controller is present.

## HHC ownership invariant

When HHC controller management is active, the addon yields controller ownership completely and remains passive.

## Restore invariant

Every system/controller state changed by the addon must have a defined restoration path.

## Ownership invariant

The addon modifies only resources it owns or changes it explicitly tracks.

## Steam Input invariant

Game-level remapping behavior belongs to Steam Input, not this application.

## Hotplug invariant

Persistent virtual-controller hotplug during one Steam session should be minimized.

## ClawTweaks invariant

ClawTweaks must not require modification for compatibility.

---

# Validation Targets

The architecture should be proven through small, isolated PoCs before substantial UI work.

Required validation areas:

1. MSI Claw PID_1902 DirectInput acquisition.
2. Correct standard controller input.
3. Independent M1/M2 input.
4. M1 = `Buttons[15]`.
5. M2 = `Buttons[16]`.
6. Classic Steam Controller `28DE:1102` creation through VIIPER.
7. Steam recognition as a Steam Controller.
8. M1/M2 exposed as independent Steam Input grips.
9. Steam Input remapping working in a normal Steam title.
10. Same behavior for a Non-Steam Shortcut launched through Steam.
11. Steam Controller remaining stable across Alt-Tab.
12. Persistent Steam Controller plus temporary Xbox360 Game Bar routing.
13. No background-game controller input while Game Bar is foreground.
14. Return from Game Bar without Steam Controller re-enumeration.
15. Clean MSI Center M restoration.
16. Clean ClawTweaks restoration.
17. External physical-controller detection.
18. External-controller hotplug veto.
19. Addon-owned VIIPER output correctly excluded from that veto.
20. HHC active controller management causing complete addon pass-through without classifying HHC virtual output as an external physical controller.
21. HHC activation during an active Steam session causing clean disengagement and a session-scoped HHC veto latch.
22. Crash recovery restoring controller/HidHide state.

These are validation requirements, not reasons to expand the product scope.

---

# Third-Party Components

The project currently links the direct NuGet dependencies declared in `src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj`:

| Component | License | Upstream |
| --- | --- | --- |
| Microsoft.WindowsAppSDK | Microsoft Software License Terms | [microsoft/WindowsAppSDK](https://github.com/microsoft/WindowsAppSDK) |
| Velopack | MIT | [velopack/velopack](https://github.com/velopack/velopack) |
| Vortice.DirectInput | MIT | [amerkoleci/Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) |

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the current component and reference inventory. Each distributed component retains its own license.

## VIIPER (planned)

[Valkirie/VIIPER](https://github.com/Valkirie/VIIPER), originally from [Alia5/VIIPER](https://github.com/Alia5/VIIPER), is the planned virtual-controller backend. Its source is licensed under `GPL-3.0-or-later`.

The intended integration is an embedded `libVIIPER.dll`; a standalone `viiper.exe` is not a required dependency. VIIPER is not necessarily included in current development builds, and no VIIPER binary is currently distributed with this project. If this project distributes a modified VIIPER build, the corresponding modified source will be made available under the applicable GPL terms.

# Reference Projects

Reference projects are used to understand hardware behavior, protocols, and established Windows controller-handling patterns. They are not dependencies or architectural templates to copy wholesale.

## Handheld Companion

Repository:

[`Valkirie/HandheldCompanion`](https://github.com/Valkirie/HandheldCompanion) (`CC-BY-NC-SA-4.0`)

Primary reference areas:

* MSI Claw hardware support;
* DirectInput handling;
* MSI Claw VID/PID behavior;
* M1/M2 identification;
* MSI controller-mode switching;
* Classic Steam Controller behavior;
* Steam Controller virtual report/protocol handling.

Reference priority:

```text
Claw hardware behavior
M1/M2
mode switching
Classic Steam Controller
→ HHC
```

HHC is also treated as a controller-owner environment at runtime: when HHC controller management is active, this addon does not attempt to reproduce or override HHC's controller virtualization.

The addon should extract only the minimum hardware/protocol knowledge required for its own narrow architecture.

HHC source is not copied, translated, or ported into this project. Hardware and protocol behavior is independently implemented. Any proposed direct reuse requires a separate license-compatibility review and preservation of the applicable copyright and license notices.

---

# DS4Windows Reference

Repository:

[`hbashton/DS4Windows`](https://github.com/hbashton/DS4Windows) (`GPL-3.0-or-later`)

Primary reference areas:

* VIIPER runtime lifecycle;
* usbip-win2 integration;
* virtual-controller creation/removal;
* hotplug handling;
* reconnect/error handling;
* HidHide integration;
* physical/virtual controller separation;
* addon-owned virtual-device tracking patterns.

One particularly important design lesson is that VIIPER/USB-IP output may look like physical USB hardware to Windows.

Therefore the addon must explicitly track its own virtual output rather than assuming Windows can always classify it as virtual.

Only the necessary lifecycle and device-separation patterns are used as a technical reference; DS4Windows architecture is not copied. Any direct source reuse must be separately reviewed and include its required copyright and license notices.

Reference priority:

```text
VIIPER lifecycle
usbip-win2
HidHide
own virtual-device exclusion
hotplug patterns
→ hbashton/DS4Windows
```

---

# Windows Platform References

For external-controller detection, the primary source of truth should remain Windows device APIs.

Preferred areas:

```text
PnP
SetupAPI
Configuration Manager APIs
Device Instance ID
Device Container ID
device relationship information
```

DS4Windows patterns may help with implementation, but external-controller classification should be built around Windows device identity rather than application-specific VID/PID lists alone.

---

# Reference Priority Summary

```text
MSI Claw DirectInput / PID / M1 / M2
→ Handheld Companion

MSI controller-mode switching
→ Handheld Companion

Classic Steam Controller
→ Handheld Companion + public Steam Controller protocol information

VIIPER runtime / lifecycle
→ hbashton/DS4Windows

usbip-win2
→ hbashton/DS4Windows

HidHide coordination
→ hbashton/DS4Windows

Addon-owned virtual-device exclusion
→ hbashton/DS4Windows

External physical-controller detection
→ Windows PnP/SetupAPI + DS4Windows patterns
```

---

# ClawTweaks Compatibility Reference

[enterTheVoidCode/ClawTweaks](https://github.com/enterTheVoidCode/ClawTweaks) is licensed upstream under GNU AGPL v3. It is an optional compatibility target, not a runtime dependency. This project does not use ClawTweaks source code, require its private IPC or internals, modify ClawTweaks, or take ownership of ClawTweaks-owned virtual devices. The two projects are independently implemented and should coexist without either project being presented as a derivative of the other.

# Third-Party Source Policy

Do not copy complete reference-project architecture into this addon.

Prefer:

```text
observe behavior
→ identify minimum required mechanism
→ implement addon-specific version
```

Direct third-party code reuse should only occur where it clearly reduces risk or avoids unnecessary reimplementation.

When third-party source code is directly incorporated:

* preserve required copyright notices;
* preserve required license notices;
* document the source;
* ensure license compatibility.

Hardware/protocol observations should be independently implemented where practical.

---

# License

Steam Input Addon for Claw is licensed under the GNU General Public License version 3 or, at your option, any later version (`GPL-3.0-or-later`).

This is an unofficial project and is not affiliated with or endorsed by MSI or Valve.

Redistributed third-party components retain their own licenses.

Reference projects may have different licensing terms. Using a project as an implementation or protocol reference does not automatically permit copying its source.

Third-party code must be reviewed individually before direct reuse.

---

# Development Principles

Routing correctness and restoration safety take priority over UI.

Development should proceed in small, independently reviewable steps.

Each functional change should include appropriate automated tests where possible.

Before merging:

```text
existing relevant tests
→ PASS

new tests
→ PASS

dotnet build
→ PASS

GitHub Actions CI
→ PASS
```

Work is performed on task-specific branches.

Do not commit feature work directly to `main`.

Each PR should document:

* changes;
* test results;
* limitations;
* required manual tests.

Do not combine unrelated future functionality into the same PR.

PRs are reviewed before merge.

---

# Decision Rules

When multiple implementations are possible, prefer the one that:

1. changes MSI/ClawTweaks native state the least;
2. is completely reversible;
3. does not require ClawTweaks modification;
4. delegates mapping/macros to Steam Input;
5. never interferes with external physical controllers;
6. yields controller ownership completely when HHC controller management is active;
7. behaves like an uninstalled addon when intervention is unnecessary;
8. minimizes virtual-controller hotplug during an active Steam session;
9. clearly distinguishes addon-owned state from third-party state;
10. has a deterministic crash-recovery path;
11. keeps the addon narrow rather than becoming a general controller manager.
