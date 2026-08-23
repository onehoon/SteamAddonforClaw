# Steam Input Addon for Claw

Steam Input Addon for Claw brings Steam Input and Steam Deck-style controller integration to the built-in controller on supported MSI Claw handhelds.

When a Steam game or Steam Big Picture session needs the controller, the Addon temporarily routes the built-in MSI controller through a virtual Steam Deck controller. When routing is no longer needed, the Claw returns to its normal native Windows controller mode.

It also provides Center M button remapping, device-level CPU Boost and TDP controls, per-game performance profiles, and controls inside Steam Quick Access Menu.

## Supported devices

| Device | Board ID | Support |
| --- | --- | --- |
| MSI Claw 7 AI+ A2VM | `MS-1T42` | Supported |
| MSI Claw 8 AI+ A2VM | `MS-1T52` | Supported |
| MSI Claw 8 EX AI+ CG3EM | `MS-1T91` | Supported |

The Addon identifies supported models by their exact MSI board ID. Unsupported or unrecognized hardware exits before the controller runtime starts and does not apply controller or performance changes.

### Hardware validation status

- **MSI Claw 8 EX AI+ CG3EM (`MS-1T91`)** — tested on physical hardware.
- **MSI Claw 7 AI+ / 8 AI+ A2VM (`MS-1T42`, `MS-1T52`)** — supported, but physical hardware validation is still pending.

## Requirements

- Windows 11 x64
- A supported MSI Claw model listed above
- Steam installed and running for Steam Input routing and Quick Access Menu integration
- The normal MSI controller environment, including MSI Center M, available on the device

Controller routing is not intended to share ownership of the built-in controller with another controller-routing or virtual-controller manager at the same time.

## Main features

- Automatic Steam Input routing for Steam games and Steam Big Picture Mode
- Virtual Steam Deck controller output (`VID 28DE`, `PID 1205`)
- Built-in controller button, stick, trigger, D-pad, and rear-button mapping
- Physical rumble support
- WING button integration as the Steam button during active routing
- Center M / OEM1 integration as Steam Quick Access during active routing
- Configurable Center M normal action outside active routing
- Device-level CPU Boost and TDP control
- Per-game CPU Boost and TDP profiles
- Steam Quick Access Menu controls
- Background tray operation and lifecycle recovery

## Steam Input Routing

Open the **Controller** tab and enable **Steam Input Routing**.

When enabled, the Addon automatically routes the built-in controller when a Steam game or Steam Big Picture Mode requires Steam Input.

### Normal operation

```text
MSI Claw built-in controller
        ↓
Native MSI / Windows controller mode
```

Outside an active Steam route, the Claw remains in its normal controller mode.

### During Steam Input Routing

```text
MSI Claw built-in controller
        ↓
MSI DirectInput mode
        ↓
Steam Input Addon for Claw
        ↓
Virtual Steam Deck controller
        ↓
Steam Input
```

The virtual Steam Deck controller is the single controller presentation used by the Addon while routing is active.

If routing cannot be established safely, the Addon does not continue with a partially owned route and returns toward the native controller state instead.

## Controller mapping

The built-in MSI Claw controls are mapped to the virtual Steam Deck controller as follows.

| MSI Claw control | Steam Deck output |
| --- | --- |
| A / B / X / Y | A / B / X / Y |
| D-pad | D-pad |
| LB / RB | L1 / R1 |
| LT / RT analog travel | L2 / R2 analog triggers |
| LT / RT full pull | L2 / R2 digital full-pull |
| Left stick | Left stick |
| Right stick | Right stick |
| L3 / R3 | L3 / R3 |
| Menu / Start | Menu |
| View / Back | Options |
| M1 right rear button | R4 |
| M2 left rear button | L4 |

Motion / gyro output is not currently part of the supported controller mapping.

## WING and Center M button behavior

The WING and Center M buttons intentionally behave differently depending on whether Steam Input Routing is active.

| Button | Routing inactive | Steam Input Routing active |
| --- | --- | --- |
| **WING** | Native Windows / Game Bar behavior | **Steam Button** |
| **Center M / OEM1** | User-configured Normal Action | **Steam Quick Access** |

The routing-time assignments are fixed. They are not user-remappable because they provide the Steam system-button behavior needed while the virtual Steam Deck controller is active.

## Center M button remapping

The **Controller** tab also contains the Center M button settings used when Steam Input Routing is inactive.

Center M remapping is managed by the Addon and is shown as **Always enabled**. The editable **Normal Action** controls what a normal Center M press does outside an active Steam route.

Available Normal Actions are:

- **None**
- **Steam Big Picture** — the default action
- **Keyboard / Hotkey** — optional Ctrl, Shift, Alt, or Win modifiers plus one key
- **Launch Application** — launches a selected `.exe`, with optional arguments

During active routing, the Normal Action is temporarily ignored and Center M always becomes **Steam Quick Access**.

### MSI Center M suppression and ownership

The MSI Center M application can change the physical controller mode. That would conflict with an active Steam route, so the Addon protects controller ownership while routing is active.

When a route is being established, the Addon checks the real MSI Center M MainUI state and prevents Center M from unexpectedly taking the controller back while the virtual Steam Deck route is owned. If that ownership boundary cannot be established safely, routing does not continue.

When Steam Input Routing is inactive, manually opening the real MSI Center M application is still allowed and native Center M behavior can take over normally. The physical Center M button itself continues to use the Normal Action configured in the Addon.

## Device tab

The **Device** tab contains global performance settings for the handheld.

These settings are the normal device-level values used when no enabled game profile is taking priority.

### CPU Boost

CPU Boost can be enabled independently and configured separately for:

- **Plugged in**
- **On battery**

The available Windows processor boost modes are presented directly in the UI.

### TDP Control

TDP Control provides separate AC and battery values for:

- **PL1**
- **PL2**

The available range is limited to the supported range for the detected Claw model.

On first use, the Addon can initialize its TDP values from the existing MSI Center M Manual TDP values when those values are available.

### What turning a Device feature off means

Turning **CPU Boost** or **TDP Control** off means the Addon stops managing that feature at the Device level.

It is not a "restore the value that existed before the Addon started" command. Saved values are kept so they are available again if the feature is re-enabled.

## Profile tab

The **Profile** tab provides per-game CPU Boost and TDP settings.

The game list is built from:

- installed Steam games
- Non-Steam shortcuts registered in Steam

Use **Refresh** if you install a game or add a new Non-Steam shortcut while the Addon is already running.

### Creating a game profile

1. Open **Profile**.
2. Search for or select a game.
3. Enable the profile using the toggle beside the game selector.
4. Configure CPU Boost for plugged-in and battery operation.
5. Configure TDP PL1 / PL2 for plugged-in and battery operation.

A profile is a complete per-game performance configuration rather than a set of individual inherited overrides.

When a profile is enabled for the first time, its initial values are copied from the saved Device values when available. After that, the game keeps its own saved values.

Disabling a profile does not erase its settings.

### Device and Profile priority

When a game with an enabled profile is running:

```text
Enabled game Profile
        ↓ takes priority over
Enabled Device setting
```

When the game exits:

- if the corresponding Device feature is enabled, the saved Device value becomes effective again;
- if the Device feature is disabled, the Addon stops managing that feature instead of restoring an older pre-game value.

Performance profiles use the actual Steam AppID and operate independently from the controller-routing switch. A game profile can therefore apply even when Steam Input Routing itself is disabled.

## Steam Quick Access Menu support

Steam Input Addon for Claw integrates its performance controls into Steam's GamepadUI / Quick Access Menu.

During active routing, press **Center M** to open Steam Quick Access.

The Addon tab provides quick access to the same performance settings used by the desktop UI:

- CPU Boost
- TDP PL1 / PL2
- active game Profile controls

When no game is active, the QAM surface exposes Device-level controls. When a supported active game is detected, it presents that game's Profile controls instead.

The QAM integration uses Steam's native GamepadUI components. If a Steam client update changes those internal components in an incompatible way, the Addon disables the affected QAM integration rather than injecting an unsupported fallback UI.

## Quick start

1. Install the official release package.
2. Launch **Steam Input Addon for Claw**.
3. Keep Steam running.
4. Open the **Controller** tab.
5. Enable **Steam Input Routing**.
6. Start a Steam game or enter Steam Big Picture Mode.
7. Use **WING** for the Steam menu and **Center M** for Steam Quick Access while routing is active.
8. Configure optional CPU Boost / TDP defaults in **Device**.
9. Configure game-specific performance settings in **Profile** if desired.

## Background operation

The controller runtime runs separately from the settings window and remains available from the system tray.

Closing the settings window does not need to stop controller routing or profile handling. Use the tray controls when you want to reopen the UI or fully exit the Addon.

## Safety and recovery

The Addon is designed around the normal handheld lifecycle rather than leaving the controller permanently in a routed state.

It handles the controller ownership and recovery path across events such as:

- entering and leaving Steam routing
- physical controller re-enumeration
- sleep / hibernate / resume
- application shutdown or restart
- routing failures and rollback

The Addon does not intentionally replay a stale routed session after startup. Controller ownership is rebuilt from the current live device state.

## Known limitations

- Physical hardware validation is currently complete on the MSI Claw 8 EX AI+ CG3EM (`MS-1T91`); A2VM models are supported but still awaiting physical-device validation.
- Motion / gyro output is not currently supported by the Steam Deck virtual-controller mapping.
- QAM integration depends on Steam GamepadUI internals and may require an Addon update after a major Steam client UI change.
- Running another application that independently takes ownership of the same physical controller can prevent Steam Input Routing from becoming active.

## Troubleshooting

If Steam Input Routing does not activate:

1. Confirm the device is one of the supported board IDs listed above.
2. Confirm **Steam Input Routing** is enabled in the Controller tab.
3. Confirm Steam is running.
4. Close other controller-routing or virtual-controller tools that may be managing the built-in controller.
5. If MSI Center M was opened or the controller was re-enumerated, allow the Addon to return to a stable native state and then start the Steam session again.

If the Steam Quick Access Addon tab is missing after a Steam client update, the Steam GamepadUI integration may need a compatibility update. Core settings remain available from the desktop UI.

## Development documentation

Technical implementation notes, protocol research, lifecycle design, and the pre-release development README are available in [`docs/`](docs/).

The previous development-oriented README is preserved as [`docs/PRE_RELEASE_DEVELOPMENT_STATUS.md`](docs/PRE_RELEASE_DEVELOPMENT_STATUS.md).

## License

This project is licensed under `AGPL-3.0-only`.
