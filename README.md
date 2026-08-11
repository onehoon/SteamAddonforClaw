# Steam Input Addon for Claw

> [!WARNING]
> This project is under active development and is not functional yet. Do not install or use it yet.

A lightweight Steam Input bridge for MSI Claw handheld PCs.

The project exposes the MSI Claw built-in controller to Steam as a **Classic Steam Controller**, allowing the rear M1/M2 buttons to appear as independent Steam Controller grip buttons.

The addon intentionally does not implement its own remapping, macros, profiles, or controller configuration system. Those functions are delegated to **Steam Input**.

## PID_1902 non-Gyro input pipeline

The PID_1902 DirectInput layout is independently normalized into device-independent controller state for the Classic Steam Controller output path. The non-Gyro pipeline covers A/B/X/Y, 8-way D-pad, LB/RB, analog and full-pull LT/RT, Back/Start, L3/R3, both sticks, and M1/M2. The Claw right stick is represented as the Classic Steam Controller right pad; R3 is right-pad click, M2 is left grip, and M1 is right grip. Gyro, accelerometer, native controller-mode switching, HidHide routing, and automatic Steam-session/Test-Mode routing remain deferred.

> Unofficial project. Not affiliated with MSI or Valve.

## Project

* **Platform:** Windows 11 24H2 or later
* **Device family:** MSI Claw handheld PCs
* **Current supported model:** MSI Claw 8 EX AI+ CG3EM (`Win32_BaseBoard.Product = MS-1T91`)
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
Viewed from the front of the device:

Left rear button  M2 → Steam Controller Left Grip
Right rear button M1 → Steam Controller Right Grip
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

## Native-state recovery

Native controller state is owned by the active handheld-device adapter. Recovery persists a device-neutral snapshot envelope with a stable device ID and an opaque device-specific payload, then selects the restoring adapter from that journaled ID rather than re-detecting the current handheld. The recovery core never interprets device-specific payloads.

MSI Claw native-state restoration currently verifies an already-restored state only; active controller mode switching and restoration remain a later hardware PoC.

## Power-transition safety

Suspend and hibernate close the addon mutation gate before controller cleanup. In-flight operations are invalidated by a power epoch. Resume performs recovery and fresh environment detection before mutations are allowed again; pre-suspend device handles and virtual-controller state are never trusted or automatically recreated.

---

# Supported Environments

## Current MVP

The current MVP supports **Stock MSI Center M only**. It requires MSI Center M to be installed and operational, with neither ClawTweaks nor Handheld Companion installed. Other controller-management environments are shown as unsupported and the addon remains passive.

Hardware support is currently limited to **MSI Claw 8 EX AI+ CG3EM**. Compatibility is determined from the exact `Win32_BaseBoard.Product` value `MS-1T91`; another board model is unsupported, and an unavailable board identity is treated as indeterminate without routing or setup mutation.

Controller environment detection and support policy are separate. Detection and classification record the controller-management environment that is observed; support policy decides whether the current addon version may mutate controller state in that environment. Stock MSI Center M and ClawTweaks are intentionally separate future routing strategies. Handheld Companion and Winhanced are unsupported owner environments; when positively detected, the addon remains passive. An indeterminate controller-management environment always fails closed to passive behavior.

This classification boundary does not add production Winhanced detection. Reliable Winhanced installation, runtime, and ownership detection is a separate follow-up.

## Planned Compatibility

**MSI Center M + ClawTweaks** remains a planned compatibility target, not a currently supported environment.

ClawTweaks support is planned compatibility behavior only.

ClawTweaks is **not a runtime dependency** and the addon must not require modifications to ClawTweaks.

The addon must also preserve unrelated ClawTweaks features such as:

* TDP controls;
* fan controls;
* OSD;
* performance controls;
* other non-controller functionality.

**Handheld Companion (HHC) is not a third addon routing mode.** It is unsupported by the current MVP when installed. Future coexistence work may permit an installed but inactive HHC environment while keeping the addon passive whenever HHC actively manages the controller.

Unless a section is explicitly labeled **Current MVP**, later ClawTweaks and HHC interaction details describe planned work and are not enabled by the current version.

---

# Core Behavioral Rule

The addon may intervene only when:

```text
External physical controller absent
AND
recovery state safe
AND
current handheld model supported
AND
current controller environment supported
AND
routing prerequisites ready
AND
Steam session active
```

Any non-eligible outcome prevents routing and mutation. The operational UI distinguishes the reason as **Passive**, **Unsupported**, **Indeterminate**, **Setup required**, or **Waiting for Steam**; these are not all the same user-visible state.

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

## Routing Pipeline Contract

Routing eligibility, action planning, and pipeline configuration are separate concerns. `RoutingDecision` determines whether routing is eligible. The existing `RoutingActionPlan` determines whether the runtime should enter or exit override. The future `RoutingPipelinePlan` describes per-stage `Disabled`, `ObserveOnly`, or `Enabled` intent for an environment-specific routing implementation.

The initial pipeline stages are `NativeMode`, `PhysicalInput`, `PhysicalIsolation`, `ThirdPartyIsolation`, `SteamOutput`, `XboxOutput`, and `GameBarRouting`. Stage modes are intentionally independently configurable so Stock MSI Center M and ClawTweaks behavior can be validated experimentally before production strategies are fixed. Recovery and external-controller veto are not optional stages; they remain mandatory cross-cutting safety requirements.

The plan does not infer stage dependencies. The generic executor uses an explicit forward order of `NativeMode` → `PhysicalInput` → `PhysicalIsolation` → `ThirdPartyIsolation` → `SteamOutput` → `XboxOutput` → `GameBarRouting`, with rollback in reverse order.

`ObserveOnly` never enters the mutation boundary. Enabled stages must successfully complete `PrepareMutationAsync` before `ExecuteMutationAsync` may perform intended routing or device mutation. A failed prepare or execute operation triggers best-effort rollback of the current and previously prepared Enabled stages in reverse order. `PrepareMutationAsync` is the generic recovery boundary; concrete stages must capture or persist the exact state required for their own safe rollback before reporting preparation success. This PR does not expand the recovery journal schema or claim mixed-stage crash recovery support.

`ControllerManagerClassification` is the canonical input for selecting an environment routing strategy. The initial strategy families are `StockCenterM`, `ClawTweaks`, and `Unsupported`: no third-party manager selects StockCenterM; ClawTweaks selects ClawTweaks; Handheld Companion, Winhanced, multiple managers, indeterminate, and unknown classifications select Unsupported.

Strategy selection does not authorize controller mutation. Routing eligibility, external-controller veto, recovery safety, prerequisite readiness, and compatibility policy remain separate mandatory gates. The current safe baselines are StockCenterM with only `NativeMode = Enabled`, and ClawTweaks/Unsupported with every stage disabled. These are baseline plans, not finalized production routing requirements, and the existence of a ClawTweaks strategy does not make ClawTweaks supported by the current compatibility policy.

Experiment options are immutable overlays on environment strategy baselines. Stock Center M and ClawTweaks options are independent: a stage override configured for one environment is never applied to the other. A null override inherits the baseline, while `Disabled`, `ObserveOnly`, or `Enabled` explicitly replaces that stage's baseline mode.

`RoutingPipelineSessionCoordinator` owns routing-session orchestration. It selects the environment strategy and effective `RoutingPipelinePlan` once when an override session enters, then freezes that effective plan for the lifetime of the session. Repeated eligible observations do not rebuild or replace an active plan, and exit/failure cleanup uses the exact plan frozen at entry. If rollback fails, the active session and frozen plan are preserved so cleanup can be retried. Strategy selection is not routing authorization.

The MSI Claw `NativeMode` stage now has a concrete pipeline adapter that reuses the existing `MsiClawNativeModeSessionCoordinator`. Observe and Prepare perform read-only preflight; Execute retains the existing recovery-before-mode-switch ordering; and rollback restores only a native session owned by the stage, retaining ownership when restore fails so cleanup can be retried. The Stock baseline remains unchanged.

`RoutingPipelineRuntimeCoordinator` is the production routing bridge. It converts the canonical `SystemStatusSnapshot` into manager classification and pipeline-session reconciliation, retires stale frozen sessions after recovery before capturing fresh status, and rolls back the frozen plan during shutdown. Runtime fail-closed is non-terminal, while shutdown is terminal; the Stock baseline remains NativeMode-only.

Runtime reconciliation is serialized across status capture and pipeline transition. Post-recovery retirement and fresh entry are one serialized transition, and shutdown is a terminal runtime boundary that prevents later reconciliation. Steam-session boundary participants run only after successful pipeline cleanup, so the NativeMode local safety veto can be reset safely between sessions. Production App routing now uses this pipeline path.

Unsupported, Handheld Companion, Winhanced, multiple-manager, indeterminate, and unknown environment strategies cannot be enabled through experiment options. Experiment options do not bypass compatibility, routing eligibility, external-controller veto, recovery safety, or prerequisite gates. Production uses `RoutingExperimentOptions.None`; options are not live mutable toggles and define no user-facing persistence or UI.

The Stock MSI Claw `PhysicalInput` stage now has a concrete PID_1902 proof-of-concept, but it is not wired into production routing and the Stock baseline still keeps `PhysicalInput` disabled. PID_1902 selection is not based on VID/PID count alone: the DirectInput interface must resolve to a verified MSI gamepad PnP interface and MSI physical root. Multiple descriptors are accepted only when they share the same verified physical identity and PnP instance; otherwise acquisition fails closed.

For this stage, ObserveOnly and Enabled Prepare perform enumeration and identity verification only. Enabled Execute acquires the exact descriptor approved during Prepare. Rollback only unacquires and disposes a session owned by the stage. PhysicalInput does not switch MSI native mode or mutate HidHide. The existing normalized `ControllerState` and independent M1/M2 mapping remain in use (`Buttons[15]` and `Buttons[16]`).

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

External-controller veto latched for this Steam session?
    YES
    → PASSIVE / VETO

    NO
    ↓

Recovery safe?
    NO
    → INDETERMINATE

    YES
    ↓

External controller assessment indeterminate?
    YES
    → INDETERMINATE

    NO
    ↓

Handheld model compatibility?
    Unsupported
    → UNSUPPORTED

    Indeterminate
    → INDETERMINATE

    Supported
    ↓

Controller environment compatibility?
    Unsupported
    → UNSUPPORTED

    Indeterminate
    → INDETERMINATE

    Supported
    ↓

Routing prerequisites ready?
    NO
    → SETUP REQUIRED

    YES
    ↓

Steam session active?
    NO
    → WAITING FOR STEAM

    YES
    ↓

Eligible for Steam Controller routing
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

External-controller veto always overrides every other state. Current-MVP controller-environment incompatibility is reported as Unsupported and does not create an external-controller veto latch.

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

# Planned ClawTweaks Compatibility

> This section describes a future compatibility target. ClawTweaks is unsupported by the current MVP when installed.

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

# Future Handheld Companion Coexistence

> This section describes a future coexistence target. Handheld Companion is unsupported by the current MVP when installed.

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
M2 → Left Grip
M1 → Right Grip
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
* an external-controller veto forces complete disengagement.

Planned future HHC coexistence may add an HHC-management transition that forces pass-through. That is not part of the Current MVP lifecycle because an installed HHC environment is currently unsupported.

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

HidHide and usbip-win2 are required prerequisites for supported addon routing. In the Current MVP, missing components in an otherwise supported Stock MSI Center M environment are shown as **Setup required**. The user starts one explicit **Install Required Components** action; one elevated helper validates the bundled installers, records durable machine-wide receipts, and installs HidHide followed by usbip-win2. Routing and automatic mutation remain disabled until setup is complete. A reboot-required installer result is shown as **Restart Windows** rather than being treated as a completed install. ClawTweaks HidHide reuse is planned future compatibility behavior only.

The pinned usbip-win2 baseline is official x64 release **0.9.7.7**. Version 0.9.7.8 is deliberately excluded from bundled and runtime-download candidates. The setup page warns that USB devices can briefly disconnect while usbip-win2 installs; the helper uses `/NORESTART` and reports restart-required rather than restarting Windows itself.

The v1 compatibility baseline is the official HidHide 1.5.230 release. The addon uses its persistent configuration API with recovery journaling; newer process/session blacklist APIs are not required by v1.

Installing HidHide alone must not hide the MSI Claw controller. While the addon is PASSIVE, it owns no HidHide device hiding or whitelist lease. In Stock MSI Center M environments the physical controller remains normally exposed; existing ClawTweaks/HHC HidHide configuration and controller exposure state are left unchanged.

Where a supported HidHide version provides process/session-scoped hiding, it may be used as an additional safety mechanism.

It must not be assumed to exist on every installed HidHide version.

## HidHide provisioning provenance

If HidHide or usbip-win2 was already present before the addon, the addon must never treat it as addon-provisioned. When the elevated helper starts an explicit first-time install from a confirmed Missing state, it writes a durable provisioning receipt before starting each installer. Receipts are stored under ProgramData in an ACL-validated, non-roaming directory separate from the Velopack root and crash-recovery data; they record installer version and hash for future uninstall-safety decisions. A legacy per-user HidHide receipt is never trusted as uninstall provenance and blocks automatic HidHide installation when HidHide is missing.

Future uninstall cleanup may remove HidHide only when the receipt and current system state prove that removal is safe. If another consumer may depend on HidHide, the configuration has changed, or provenance is uncertain, HidHide must be preserved.

The same conservative rule applies to usbip-win2. A receipt only records that this addon initiated a pinned installer from a missing state; it does not establish exclusive ownership. Future removal must preserve usbip-win2 whenever another consumer, a version change, configuration change, or provenance uncertainty is present. This PR does not implement package removal.

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

A recovery session may contain multiple recorded mutations. Each mutation's
 recovery evidence is persisted before the corresponding change, and successful
 stage rollback clears only the mutation owned by that stage. The journal is
 deleted only after all recorded mutations have been cleared. Current mixed
 crash-recovery support is limited to native device state and HidHide
 executable whitelist additions. HidHide device entries and virtual-output
 recovery remain unsupported and fail closed. Recovery schema version 2 is
 retained; this extends the lifecycle of the existing state rather than its
 serialization format.

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

## VIIPER

The Developer-only Classic Steam Controller lifecycle PoC bundles `libVIIPER.dll` built directly from [onehoon/VIIPER](https://github.com/onehoon/VIIPER) tag `steam-input-addon-baseline-1`. That immutable tag points to the Valkirie baseline `209c882009caea4f3baf322b9b6020c1a921feed`, whose lineage is [Valkirie/VIIPER](https://github.com/Valkirie/VIIPER) and [Alia5/VIIPER](https://github.com/Alia5/VIIPER). VIIPER is GPL-3.0.

The embedded DLL is loaded only through an absolute path using `NativeLibrary.Load` and required C exports. A standalone `viiper.exe` is not required. Handheld Companion's bundled DLL is not redistributed or used as a dependency; HHC remains a behavioral reference only.

The PoC creates synthetic `28DE:1102` input and offers only neutral, Left Grip, and Right Grip reports. It does not read physical controller input, perform routing, change MSI controller mode, or mutate HidHide. Windows PnP identity, Steam recognition, independent grip behavior, repeated lifecycle, and crash cleanup require explicit hardware validation before this runtime can be promoted beyond the developer menu.

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
