# Steam Input Addon for Claw — Project Instructions

## 1. Project Identity

* **Project name:** Steam Input Addon for Claw
* **GitHub repository:** `onehoon/SteamInputAddonforClaw`
* **Repository:** Public
* **Target OS:** Windows 11
* **Target devices:** MSI Claw handheld PCs
* **License:** GPL-3.0-or-later

This is an unofficial project and is not affiliated with MSI or Valve.

---

## 2. Project Purpose

Steam Input Addon for Claw is a lightweight companion application that enables the MSI Claw built-in controller, including its M1/M2 rear buttons, to work through Steam Input.

The application must not become a general controller remapping or macro application.

For Steam-launched games:

* Controller remapping is handled by Steam Input.
* Macros are handled by Steam Input.
* Action Sets, long press, double press, keyboard mappings, etc. are handled by Steam Input.
* M1/M2 must be exposed to Steam Input as independent Steam Controller grip buttons.

For non-Steam use, the application should behave as though it is not installed.

The application must support both:

1. MSI Center M stock environments.
2. MSI Center M + ClawTweaks environments.

ClawTweaks integration is optional compatibility behavior, not a hard runtime dependency.

---

## 3. Core Behavioral Principle

The application should intervene only when all of the following are true:

1. A game or Non-Steam Shortcut launched through Steam is currently running.
2. No external physical controller is connected.

Otherwise the application must remain passive.

Priority:

```text
External Controller Present?
    YES → Do nothing.

Steam Game Running?
    NO → Do nothing.

Otherwise
    → Activate Steam Input bridge.
```

“Do nothing” should mean as close as practical to the application not being installed:

* No virtual controller exposed.
* No physical controller acquired unnecessarily.
* No MSI controller mode changes.
* No HidHide configuration changes.
* No modification of the user's normal Center M or ClawTweaks behavior.

---

## 4. Steam Session Detection

Do not identify individual games unless later required.

The current requirement is only to determine:

> Is Steam currently running a game?

Use the Steam client registry state:

```text
HKCU\Software\Valve\Steam
RunningAppID
```

Observed behavior:

```text
RunningAppID == 0
→ No Steam game session.

RunningAppID != 0
→ Steam game session active.
```

This has been verified to work for:

* Normal Steam games.
* Non-Steam games/shortcuts launched through Steam.

Prefer event-driven registry monitoring where practical instead of unnecessarily frequent polling.

Foreground application changes must NOT terminate the Steam session.

Example:

```text
Steam game
→ Explorer
→ Edge
→ Discord
→ Steam
→ Game

Steam virtual controller remains active for the entire Steam session.
```

The Steam session ends only when `RunningAppID` returns to 0.

---

## 5. External Controller Veto

External physical controller presence has higher priority than Steam session detection.

If an external controller is connected:

```text
Steam Input Addon = completely inactive
```

This applies even if a Steam game is running.

Examples include:

* Xbox controllers.
* DualSense / DualShock.
* 8BitDo controllers.
* Other external physical gamepads.

Do not treat these as external controllers:

* MSI Claw internal controller interfaces.
* The application's own VIIPER devices.
* ClawTweaks virtual devices.
* USB/IP virtual devices.
* ViGEm virtual devices.

Prefer PnP/SetupAPI device enumeration and physical device/container identification rather than determining external controller presence from XInput slot count.

The application should not acquire external controller input handles.

### External controller connected during an active Steam session

If an external controller is connected while the addon is active:

1. Immediately stop the Steam Input bridge.
2. Remove the addon's virtual controllers.
3. Restore the MSI/ClawTweaks native controller state.
4. Set an `ExternalControllerVeto` for the remainder of that Steam session.

If the external controller is disconnected again during the same Steam session, do NOT automatically reactivate the addon.

Wait until the next Steam session.

This avoids repeated controller hotplug/re-enumeration.

---

## 6. MSI Claw Physical Input Source

The preferred physical input source is the MSI Claw DirectInput interface.

Known MSI Claw controller interfaces:

```text
VID: 0x0DB0

PID 0x1901 → XInput
PID 0x1902 → DirectInput
PID 0x1903 → testing/other mode
```

Handheld Companion currently uses a dedicated MSI Claw DirectInput controller implementation.

The known DirectInput mappings include:

```text
M1 → DirectInput Button[15]
M2 → DirectInput Button[16]
```

Use this architecture as a hardware/protocol reference.

Do not depend on reading M1/M2 from an XInput virtual controller because XInput cannot expose these additional rear buttons independently.

The addon should maintain an internal normalized `ControllerState` containing at minimum:

```text
A / B / X / Y
D-Pad
LB / RB
LT / RT
Left Stick
Right Stick
L3 / R3
Start / Select
M1
M2
```

---

## 7. MSI Center M Stock Environment

When no Steam session is active, leave MSI Center M and the internal controller completely untouched.

When a Steam session starts and no external controller is present:

1. Snapshot the current controller/environment state.
2. If required, switch the MSI Claw internal controller to DirectInput mode.
3. Wait for PID_1902 to become available.
4. Acquire only the MSI Claw DirectInput controller.
5. Hide the appropriate physical gamepad interface from games if required.
6. Create the virtual Steam Controller.
7. Route Claw input into it.

When the Steam session ends:

1. Remove temporary XInput output if present.
2. Remove the Steam Controller.
3. Release DirectInput.
4. Restore HidHide changes made by this application.
5. Restore the original MSI controller mode.

The result should be indistinguishable from the addon not being active.

Never hard-code the assumption that the original controller mode was XInput; snapshot and restore it.

---

## 8. ClawTweaks Environment

ClawTweaks currently uses controller virtualization and HidHide, and its latest installed builds include:

```text
libviiper.dll
SharpDX.DirectInput.dll
HidSharp.dll
Nefarius.Drivers.HidHide.dll
```

usbip-win2 is installed by current ClawTweaks.

ViGEmBus may not be installed and must not be assumed to exist.

Current ClawTweaks appears to use embedded `libVIIPER` rather than a standalone `viiper.exe`.

Do not depend on ClawTweaks private implementation details.

Do not attempt to mutate or steal a ClawTweaks-owned VIIPER device.

During a Steam override session:

* ClawTweaks controller mapping/macros are intentionally not used.
* Steam Input is responsible for game controller mapping and macros.
* Prevent ClawTweaks virtual controller output from causing duplicate game input.
* Preserve all unrelated ClawTweaks functionality such as TDP, OSD, fan control, performance settings, etc.

After the Steam session, restore normal ClawTweaks controller behavior.

---

## 9. Virtual Steam Controller

Initial target:

```text
Classic Steam Controller
Valve VID/PID: 28DE:1102
```

Do not prioritize the newer 2026 Steam Controller/Triton protocol for the first implementation.

Classic Steam Controller is preferred because:

* HHC already proves VIIPER-based Steam Controller emulation is practical.
* It exposes two rear grip inputs, which map naturally to the Claw's M1/M2.
* It avoids implementing the newer Triton HID protocol.

Recommended fixed physical mapping:

```text
Claw M1 → Steam Controller Left Grip
Claw M2 → Steam Controller Right Grip
```

The addon must NOT implement per-game mapping for these inputs.

Steam Input handles all mapping after this point.

---

## 10. Steam Input Responsibility

Once the virtual Steam Controller is active:

```text
MSI Claw
    ↓
Addon normalized input
    ↓
Virtual Steam Controller
    ↓
Steam
    ↓
Steam Input
    ↓
Game
```

Steam Input is responsible for:

* Per-game layouts.
* Button remapping.
* Keyboard/mouse mapping.
* Macros.
* Turbo.
* Long press.
* Double press.
* Action Sets / Action Layers.
* Radial menus.
* Other Steam Input functionality.

Do not duplicate these functions inside the addon.

---

## 11. Xbox Game Bar Exception

Xbox Game Bar / ClawTweaks navigation currently requires XInput.

During an active Steam session, the virtual Steam Controller should remain enumerated for the entire game session.

Do NOT disconnect/reconnect the Steam Controller every time Game Bar opens.

When Xbox Game Bar becomes foreground:

```text
Virtual Steam Controller
→ remains connected
→ receives neutral input reports

Temporary Xbox 360 virtual controller
→ created/enabled
→ receives live MSI Claw input
```

This allows Game Bar and ClawTweaks UI navigation while preventing the background Steam game from reacting to the same controller inputs.

When Game Bar leaves foreground:

```text
Temporary Xbox 360 controller
→ removed

Virtual Steam Controller
→ remains connected
→ resumes receiving live controller reports
```

The Steam Controller should remain the same enumerated device throughout the Steam session to avoid hotplug, Steam Input rebinding, player-slot and device-lost issues.

---

## 12. Game Bar Detection

Do not require modifications to ClawTweaks.

The addon must be able to detect Xbox Game Bar independently.

Prefer event-driven foreground monitoring such as:

```text
SetWinEventHook(EVENT_SYSTEM_FOREGROUND)
```

Resolve the foreground HWND to its owning process/package and identify Xbox Game Bar.

Normal foreground changes such as:

```text
Explorer
Edge
Discord
Steam Client
```

must have no effect on the Steam Controller state.

Only Xbox Game Bar is a special routing state.

---

## 13. VIIPER Architecture

Prefer embedded `libVIIPER.dll`.

Do not require a visible standalone `viiper.exe`.

VIIPER is responsible for virtual USB controller creation through usbip-win2.

The addon may need to create two VIIPER devices during Game Bar use:

```text
Persistent:
Classic Steam Controller

Temporary:
Xbox 360 controller
```

The implementation should therefore not blindly copy HHC's single-target `VirtualManager` design.

Use a small addon-specific VIIPER host/output manager capable of independently controlling both virtual outputs.

---

## 14. HidHide Rules

HidHide may already be installed and configured by ClawTweaks or another application.

Never take exclusive ownership of the complete HidHide configuration.

Rules:

* Add only entries required by this application.
* Track exactly which entries this application added.
* Remove/restore only those entries.
* Never replace the user's complete HidHide device/application list.
* Whitelist the addon itself where required to access a hidden MSI physical controller.
* Preserve existing ClawTweaks/HHC/HidHide configuration.

---

## 15. Crash Recovery

Controller state restoration is mandatory.

Before entering Steam override mode, write a recovery journal containing the state this application changed.

Example:

```text
OverrideActive
Original MSI controller mode
HidHide changes made by this app
Virtual output state
```

On normal Steam-session exit:

* Restore everything.
* Mark the journal clean.

On addon startup:

* If an incomplete previous override is detected, recovery must run before normal controller routing begins.

The addon must never leave the MSI internal controller permanently hidden or stuck in DirectInput mode after a crash.

---

## 16. Dependencies

For ClawTweaks systems, some dependencies may already exist.

For MSI Center M-only systems, the addon installer must be able to check/install required dependencies.

Expected components:

```text
libVIIPER
usbip-win2
HidHide
DirectInput support/library
```

Do not require ViGEmBus.

Installed drivers alone must not alter normal controller behavior while the addon is passive.

---

## 17. Licensing / Source Reuse

Project license:

```text
GPL-3.0-or-later
```

`libVIIPER` embedded use is compatible with choosing GPL for the application.

Handheld Companion is useful as a protocol and architecture reference, particularly for:

* MSI Claw DirectInput handling.
* M1/M2 button identification.
* MSI controller mode switching.
* Steam Controller VIIPER output behavior.

However, HHC is licensed under CC BY-NC-SA 4.0.

Therefore:

* Do not blindly copy HHC source files into this project.
* Prefer independent implementations based on documented behavior, hardware observations, public protocols and VIIPER interfaces.
* Clearly retain any required third-party notices/licenses for redistributed binaries or libraries.

ClawTweaks source should likewise be treated as a compatibility reference, not as a required source dependency.

---

## 18. Architecture Direction

Keep the application small.

Suggested components:

```text
SteamInputAddonforClaw
│
├─ SteamSessionWatcher
│    └─ RunningAppID monitoring
│
├─ ExternalControllerDetector
│
├─ MsiClawInputSource
│    └─ PID_1902 DirectInput
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
├─ MsiControllerModeManager
│
├─ EnvironmentDetector
│    ├─ CenterMEnvironment
│    └─ ClawTweaksEnvironment
│
└─ RecoveryManager
```

Avoid adding:

* Game database.
* Per-game addon profiles.
* Mapping editor.
* Macro editor.
* Action-set editor.

Those are intentionally delegated to Steam Input.

---

## 19. Primary State Machine

Conceptually:

```csharp
if (ExternalControllerVeto)
{
    RestoreNativeState();
    return;
}

if (!SteamSessionActive)
{
    RestoreNativeState();
    return;
}

EnsureSteamOverrideActive();

if (GameBarForeground)
{
    NeutralizeSteamController();
    EnsureXbox360Companion();
}
else
{
    RemoveXbox360Companion();
    ResumeSteamController();
}
```

State interpretation:

```text
PASSIVE
- Addon behaves as uninstalled.

STEAM
- Built-in Claw input routed to Classic Steam Controller.
- Steam Input owns game mappings/macros.

STEAM + GAME BAR
- Steam Controller stays connected but neutral.
- Temporary Xbox 360 output handles Game Bar navigation.

EXTERNAL CONTROLLER VETO
- Addon fully disengages for the rest of the current Steam session.
```

---

## 20. Initial Proof-of-Concept Priorities

Before building substantial UI, verify these in order:

1. Read MSI Claw PID_1902 through DirectInput.
2. Confirm all standard controls work.
3. Confirm M1 = Button 15 and M2 = Button 16 on supported Claw hardware.
4. Create a Classic Steam Controller through VIIPER.
5. Confirm Steam recognizes it as a Steam Controller.
6. Confirm M1/M2 appear as independent Steam Input grip buttons.
7. Confirm Steam Input mappings work inside Steam games.
8. Confirm the same behavior for Non-Steam shortcuts launched through Steam.
9. Keep the Steam Controller connected while temporarily creating an Xbox 360 VIIPER device.
10. Verify Xbox Game Bar/ClawTweaks can be navigated through the temporary XInput device.
11. Verify returning from Game Bar resumes the same Steam Controller without re-enumeration.
12. Verify Steam session exit restores Center M/ClawTweaks native behavior.
13. Verify connecting an external physical controller disengages the addon cleanly.
14. Verify crash recovery restores the native controller state.

UI and updater work should come after the controller-routing PoC is proven.

---

## 21. Development Decision Rule

When evaluating implementation choices, prefer the solution that:

1. Changes the native MSI/ClawTweaks state the least.
2. Is completely reversible.
3. Does not require modifying ClawTweaks.
4. Does not duplicate Steam Input functionality.
5. Does not interfere with external controllers.
6. Leaves the machine behaving normally whenever Steam override is not required.
7. Minimizes virtual-controller hotplug during an active Steam session.
