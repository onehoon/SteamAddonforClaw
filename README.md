# Steam Input Addon for Claw

> [!WARNING]
> This project is under active development and is not functional yet. Do not install or use it yet.

A lightweight Steam Input bridge for MSI Claw handheld PCs.

The project exposes the MSI Claw built-in controller to Steam as a **Classic Steam Controller**, allowing the rear M1/M2 buttons to appear as independent Steam Controller grip buttons.

The addon intentionally does not implement its own remapping, macros, profiles, or controller configuration system. Those functions are delegated to **Steam Input**.

The normal Stock Center M routing path can create a recoverable embedded-VIIPER Classic Steam Controller (`28DE:1102`) after verifying its Windows PnP identity and addon ownership. When the physical PID_1902 source is active, the same owned device receives normalized non-Gyro `ControllerState` reports from an independent nominal 250 Hz publisher; the publisher is stopped before virtual-device removal. Real Steam sessions and Developer Test Mode use the same production routing plan.

## PID_1902 non-Gyro input pipeline

The PID_1902 DirectInput layout is independently normalized into device-independent controller state for the Classic Steam Controller output path. The non-Gyro pipeline covers A/B/X/Y, 8-way D-pad, LB/RB, analog and full-pull LT/RT, Back/Start, L3/R3, both sticks, and M1/M2. The Claw right stick is represented as the Classic Steam Controller right pad; R3 is right-pad click, M2 is left grip, and M1 is right grip. Gyro and accelerometer input remain deferred. Native controller-mode switching, recoverable HidHide routing, and automatic RunningAppID-based Steam-session routing are implemented; Developer Test Mode remains a synthetic session source for development and tests only.

This MVP path does not include gyro, accelerometer, rumble, haptics, Game Bar temporary Xbox360 routing, ClawTweaks production compatibility, or production auto-enable of the complete routing pipeline. Hardware success and Steam recognition remain subject to MSI Claw validation.

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

## Startup baseline and recovery

For the supported Stock MSI Center M environment, every new application process starts from the current physical controller state rather than a previous routing session. After the update gate, compatibility checks, supported-environment verification, and topology stabilization, the addon establishes the live MSI Claw baseline: XInput / PID_1901. If the safely identified live Claw is already PID_1901, no mode write occurs. If it is PID_1902, the existing MSI mode-switch primitive converges it to PID_1901 and verifies re-enumeration. Weak identity, PID_1903, unknown topology, and unsupported environments fail closed without a mode write.

Previous journal state is not desired current state. Previous routing state is not the current routing decision. At application startup or restart, Stock MSI Center M does not resume or reconstruct a prior routing session, restore its exact native state, replay HidHide changes, or use earlier RunningAppID data. After the independently verified live XInput baseline, stale previous-process journal bookkeeping is discarded without interpreting or restoring its payload. If that discard fails, the verified physical controller remains XInput and new routing is passive.

The normal runtime then reads the current RunningAppID, applies the current eligibility checks, and may create a completely new routing session. Recovery journaling remains in use for mutations made by an active runtime session and for power-transition handling; it is not the startup target authority.

## Power-transition safety

Suspend and hibernate close the addon mutation gate before controller cleanup. In-flight operations are invalidated by a power epoch. Resume performs recovery and fresh environment detection before mutations are allowed again; pre-suspend device handles and virtual-controller state are never trusted or automatically recreated.

---

# Supported Environments

## Current MVP

The current MVP supports **Stock MSI Center M only**. It requires MSI Center M to be installed and operational, with neither ClawTweaks nor Handheld Companion installed. Other controller-management environments are shown as unsupported and the addon remains passive.

Hardware support is currently limited to **MSI Claw 8 EX AI+ CG3EM**. Compatibility is determined from the exact `Win32_BaseBoard.Product` value `MS-1T91`; another board model is unsupported, and an unavailable board identity is treated as indeterminate without routing or setup mutation.

Startup and runtime use the same controller-software assessment. It captures the current MSI Center M, ClawTweaks, and Handheld Companion status, derives the controller-manager classification, then applies the current compatibility policy. The only supported mutation environment is operational Stock MSI Center M with no unsupported third-party controller manager. ClawTweaks, Handheld Companion, other unsupported managers, and indeterminate software state remain passive. Environment detection does not use separate startup and runtime decision engines.

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

External physical controller presence or absence is **not** a routing eligibility input. The addon never inspects other controllers to decide whether to route, and never acquires, hides, or otherwise mutates them.

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

Routing eligibility and pipeline configuration are separate concerns. `RoutingDecision` determines whether routing is eligible. `RoutingPipelineSessionCoordinator` directly enters, exits, or retains the current session from that decision. `RoutingPipelinePlan` describes the fixed per-stage `Disabled`, `ObserveOnly`, or `Enabled` baseline for an environment-specific routing implementation.

The initial pipeline stages are `NativeMode`, `PhysicalInput`, `PhysicalIsolation`, `ThirdPartyIsolation`, `SteamOutput`, `XboxOutput`, and `GameBarRouting`. `RoutingPipelineSessionCoordinator` selects the fixed baseline plan directly from the already-classified controller-manager environment when a session enters. Recovery safety and addon-owned VIIPER output identity safety are not optional stages; they remain mandatory cross-cutting safety requirements. External physical-controller presence is not a pipeline input.

The plan does not infer stage dependencies. The generic executor uses an explicit forward order of `NativeMode` → `PhysicalInput` → `PhysicalIsolation` → `ThirdPartyIsolation` → `SteamOutput` → `XboxOutput` → `GameBarRouting`. Rollback uses the explicit dependency order `GameBarRouting` → `XboxOutput` → `SteamOutput` → `ThirdPartyIsolation` → `PhysicalInput` → `NativeMode` → `PhysicalIsolation`: a failed `SteamOutput`, `PhysicalInput`, or `NativeMode` rollback blocks dependent teardown, while unrelated-stage rollback failures remain best effort.

`ObserveOnly` never enters the mutation boundary. Enabled stages must successfully complete `PrepareMutationAsync` before `ExecuteMutationAsync` may perform intended routing or device mutation. A failed prepare or execute operation triggers rollback of the current and previously prepared Enabled stages in the same canonical dependency order. `PrepareMutationAsync` is the generic preflight boundary; each concrete stage must persist mutation recovery intent before entering a non-trivial mutation state, then checkpoint and clear only its own recorded mutation after verified rollback.

`ControllerManagerClassification` is the canonical input for selecting the fixed session plan. `None` selects the StockCenterM plan; ClawTweaks selects the all-disabled ClawTweaks framework plan; Handheld Companion, Winhanced, multiple managers, indeterminate, and unknown classifications fail closed as Unsupported.

Plan selection does not authorize controller mutation. Routing eligibility, recovery safety, prerequisite readiness, and compatibility policy remain separate mandatory gates. The current StockCenterM baseline enables `NativeMode`, `PhysicalInput`, `PhysicalIsolation`, and `SteamOutput`; ClawTweaks retains every stage disabled. The enabled StockCenterM baseline is used only for an eligible real Steam session, and the all-disabled ClawTweaks framework plan does not make ClawTweaks supported by the current compatibility policy.

`RoutingPipelineSessionCoordinator` is the single runtime owner of routing-session state and serializes reconciliation. `ActiveSession == null` means Passive; `ActiveSession != null` means OverrideActive. It selects and freezes the fixed `RoutingPipelinePlan` once when an override session enters. Repeated eligible observations do not rebuild or replace an active plan, and exit/failure cleanup uses the exact plan frozen at entry. If rollback fails, the active session and frozen plan are preserved so cleanup can be retried. Pending cleanup records incomplete rollback work but does not create another operational-state authority. Plan selection is not routing authorization.

The MSI Claw `NativeMode` stage now has a concrete pipeline adapter that reuses the existing `MsiClawNativeModeSessionCoordinator`. Observe and Prepare perform read-only preflight; Execute retains the existing recovery-before-mode-switch ordering; and rollback restores only a native session owned by the stage, retaining ownership when restore fails so cleanup can be retried.

`RoutingPipelineRuntimeCoordinator` is the production routing bridge. It converts the canonical `SystemStatusSnapshot` into manager classification and pipeline-session reconciliation, retires stale frozen sessions after recovery before capturing fresh status, and rolls back the frozen plan during shutdown. Runtime fail-closed is non-terminal, while shutdown is terminal.

Runtime reconciliation is serialized across status capture and pipeline transition. Post-recovery retirement and fresh entry are one serialized transition, and shutdown is a terminal runtime boundary that prevents later reconciliation. Steam-session boundary participants run only after successful pipeline cleanup, so the NativeMode routing-fault latch can be reset safely between sessions. Production App routing now uses this pipeline path.

User-initiated Exit and Restart are disabled while the addon owns live routing mutations, recovery state, or pending routing cleanup. Termination availability is derived from addon ownership and transition state, not directly from RunningAppID. After successful rollback and restoration, Exit and Restart become available again. A frozen pipeline `ActiveSession` alone is not treated as physical mutation ownership.

Unsupported, Handheld Companion, Winhanced, multiple-manager, indeterminate, and unknown environments fail closed and do not bypass compatibility, routing eligibility, recovery safety, or prerequisite gates. Stock Center M enables `NativeMode`, `PhysicalInput`, `PhysicalIsolation`, and `SteamOutput` as its production baseline. ClawTweaks retains an all-disabled framework plan but remains blocked by the current production compatibility policy. Developer Test Mode is a synthetic Steam-session source for quickly starting and stopping the Stock production path; it does not bypass safety gates.

The Stock MSI Claw `PhysicalInput` stage has a concrete PID_1902 implementation and publishes only its exact owned immutable identity. The production StockCenterM plan now runs `NativeMode`, `PhysicalInput`, recoverable `PhysicalIsolation`, and `SteamOutput` for an eligible session. PID_1902 selection is not based on VID/PID count alone: the DirectInput interface must resolve to a verified MSI gamepad PnP interface and MSI physical root. Multiple descriptors are accepted only when they share the same verified physical identity and PnP instance; otherwise acquisition fails closed.

When enabled, `PhysicalIsolation` adds only the already-acquired PID_1902 primary gamepad collection (`HID\VID_0DB0&PID_1902&MI_00&COL01\...`) to HidHide. The MSI physical root remains topology evidence and is never a HidHide target. PID_1901 and sibling PID_1902 collections are never hidden. The addon first acquires its exact executable whitelist lease, preserves all foreign HidHide entries, and may temporarily activate HidHide only from a verified disabled configuration with an empty blocked-device list. If foreign blocked entries would be affected by restoring HidHide to inactive, recovery remains passive and retains its evidence.

For this stage, ObserveOnly and Enabled Prepare perform enumeration and identity verification only. Enabled Execute acquires the exact descriptor approved during Prepare. Rollback only unacquires and disposes a session owned by the stage. PhysicalInput does not switch MSI native mode or mutate HidHide. The existing normalized `ControllerState` and independent M1/M2 mapping remain in use (`Buttons[15]` and `Buttons[16]`).

---

# State Priority

Conceptually:

```text
Recovery safe?
    NO
    → INDETERMINATE

    YES
    ↓

Addon-owned VIIPER output identity uncertain?
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

External physical-controller presence or absence does not appear anywhere in this decision chain. Connecting or disconnecting an external controller (Xbox controller, DualSense, a real Steam Controller, etc.) while the addon is routing has no effect on any of these states; the addon keeps routing the MSI Claw internal controller through Gordon and leaves the external controller alone.

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

The Steam override ends only when `RunningAppID` returns to `0`, unless another higher-priority pass-through condition (such as active HHC controller management) occurs first.

Registry monitoring should be event-driven where practical.

---

# External Controllers

External physical game controllers (Xbox controllers, DualSense, DualShock, 8BitDo controllers, a real Steam Controller, other USB/Bluetooth gamepads, etc.) are **not a routing input**. The addon does not detect, classify, veto on, acquire, hide, or otherwise mutate them. They remain normally available to Windows and to Steam at all times, whether or not the addon is actively routing the MSI Claw internal controller.

This is a deliberate simplification from earlier design iterations of this project, which treated external-controller presence as a session-scoped veto. That veto has been removed: it is no longer part of the routing eligibility model, the active-session safety monitor, startup detection, or the status UI. See [Addon-Owned Virtual Device Tracking](#addon-owned-virtual-device-tracking) below for the distinct, still-active safety concern of correctly identifying the addon's *own* virtual output.

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

Correctly identifying the addon's own virtual output remains a mandatory safety concern independent of any external-controller detection: if the addon cannot verify which VIIPER device it just created (for example, because a matching candidate device that isn't verifiably ours is still present after a failed teardown), routing must fail safe rather than guess. This is enforced directly in the routing eligibility policy — see `RoutingPolicyInput.AddonOwnedOutputIdentityUncertain` in `RoutingEligibilityPolicy` — independently of whatever other controllers happen to be connected.

Useful identities may include:

* device path;
* PnP instance ID;
* container ID;
* VID/PID;
* parent/child device relationships.

VID/PID alone should not be considered sufficient identity.

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

HHC-owned virtual controllers must not be mistaken for the MSI Claw internal controller or for addon-owned output.

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

Once created for an eligible Steam session, the Classic Steam Controller should remain enumerated until the Steam session ends. External controllers connecting or disconnecting do not affect its lifecycle at all.

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

The v1 compatibility baseline is the official HidHide 1.5.230 release. The addon uses its persistent configuration API with recovery journaling; newer process/session blacklist APIs are not required by v1. The recoverable HidHide device-mutation foundation preserves unrelated entries and tracks only exact entries newly owned by the addon; the PhysicalIsolation stage remains disabled.

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

On Stock MSI Center M application startup, a stale journal is never replayed. The live physical controller is first converged to XInput / PID_1901, then stale bookkeeping is discarded and current-world routing eligibility is evaluated from scratch.

A recovery session may contain multiple recorded mutations. Each mutation's
 recovery evidence is persisted before the corresponding change, and successful
 same-process rollback clears only the mutation owned by that stage. The journal
 is deleted only after all recorded mutations have been cleared. During a safe
 same-process power-transition recovery, virtual-output absence is verified
 before the owned runtime mutations are rolled back in dependency order. A
 still-present journaled VIIPER device blocks lower cleanup; a failed native
 rollback keeps PhysicalIsolation evidence intact; and a later
 PhysicalIsolation failure preserves its evidence after the native checkpoint.
 Recovery schema version 4 is the current format. This is not an application
 startup or crash-session reconstruction path.

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

## External-controller non-interference invariant

The addon never acquires, hides, or otherwise mutates an external physical controller, and external-controller presence or absence never changes addon routing eligibility, session state, or the UI.

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
17. External physical-controller connect/disconnect during an active session causes no routing change, no Gordon teardown, and no HidHide/native-state rollback.
18. Addon-owned VIIPER output identity uncertainty still fails routing closed, independent of external-controller presence.
19. HHC active controller management causing complete addon pass-through without classifying HHC virtual output as an external physical controller.
20. HHC activation during an active Steam session causing clean disengagement and a session-scoped HHC veto latch.
21. Crash recovery restoring controller/HidHide state.

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

The embedded runtime creates `28DE:1102` input, accepts the normalized non-Gyro report stream, and preserves the recoverable lifecycle. It does not implement gyro or accelerometer input, battery monitoring, or independent virtual-device recovery. Windows PnP identity, Steam recognition, independent grip behavior, repeated lifecycle, and crash cleanup require explicit hardware validation before this runtime can be promoted beyond the developer menu.

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

For MSI Claw internal-controller identification, addon-owned VIIPER output identity, and controller-environment stabilization, the primary source of truth should remain Windows device APIs.

Preferred areas:

```text
PnP
SetupAPI
Configuration Manager APIs
Device Instance ID
Device Container ID
device relationship information
```

DS4Windows patterns may help with implementation, but device classification should be built around Windows device identity rather than application-specific VID/PID lists alone. This addon does not classify or detect external physical controllers for routing purposes; see [External Controllers](#external-controllers).

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

Addon-owned virtual-device exclusion and ownership safety
→ hbashton/DS4Windows
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
