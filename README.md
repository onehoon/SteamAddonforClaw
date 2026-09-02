# Steam Addon for Claw

> [!WARNING]
> **Under active development — do not install or use.**
> This project is unreleased and unstable. It changes controller, driver, and
> Windows startup state on your machine, and the current architecture is being
> reworked. Installing or running it now can leave your controller or system in
> an inconsistent state. There is no supported release yet.

Steam Addon for Claw brings Steam Input and Steam Deck-style controller integration to the built-in controller on supported MSI Claw handhelds.

While the Addon holds controller authority (MSI Center M disabled), it continuously presents the built-in MSI controller to Windows. When a Steam game or Steam Big Picture session is active, that presentation is a virtual Steam Deck controller; otherwise it is a virtual Xbox 360 controller. There is no user switch for this — presentation follows the Steam/Big Picture state automatically.

Center M button remapping, device-level CPU Boost and TDP controls, per-game performance profiles, and Steam Quick Access Menu performance controls are independent features.

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
- Steam installed and running for Steam Deck controller presentation, per-game Steam profiles, and Quick Access Menu integration
- The stock MSI controller environment with MSI Center M installed and available

> [!CAUTION]
> **Steam Addon for Claw supports only the stock MSI Center M controller environment.**
>
> Do not use it together with **Handheld Companion, ClawTweaks, or similar software** that manages the built-in controller, changes its controller mode, performs controller routing, or adds another virtual-controller ownership layer. These configurations are not supported and can conflict with controller ownership, routing, recovery, or teardown.

## Main features

- Automatic Steam Deck controller presentation for Steam games and Steam Big Picture Mode
- Automatic Xbox 360 controller presentation outside Steam games and Big Picture Mode
- Virtual Steam Deck controller output (`VID 28DE`, `PID 1205`)
- Built-in controller button, stick, trigger, D-pad, and rear-button mapping
- Physical rumble support
- Configurable Center M normal action
- Device-level CPU Boost, Windows 11 Power Mode, and TDP control as independent features
- Per-game CPU Boost, Windows 11 Power Mode, and TDP profiles as independent features
- Event-driven Steam game detection without periodic game/process polling
- Per-game profiles for installed Steam games and Non-Steam games added to Steam
- Steam Quick Access Menu controls
- Automatic silent update checks at application startup
- Background tray operation and lifecycle recovery

## Steam controller presentation

While MSI Center M is disabled, the Addon holds controller authority and always presents one virtual controller. Which one it presents is decided automatically:

```text
MSI Center M disabled  →  Steam Addon for Claw controller authority
        │
        ├── Steam game running OR Steam Big Picture active  →  virtual Steam Deck controller
        │
        └── otherwise                                       →  virtual Xbox 360 controller
```

There is no user-configurable switch for this. Presentation follows the live Steam / Big Picture state.

To restore the stock MSI controller environment, re-enable MSI Center M from the **Controller** tab.

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

## Center M button remapping

The **Controller** tab contains the Center M button settings.

Center M remapping is managed by the Addon and is shown as **Always enabled**. The editable **Normal Action** controls what a normal Center M press does.

Available Normal Actions are:

- **None**
- **Steam Big Picture** — the default action
- **Keyboard / Hotkey** — optional Ctrl, Shift, Alt, or Win modifiers plus one key
- **Launch Application** — launches a selected `.exe`, with optional arguments

### MSI Center M and controller ownership

The MSI Center M application can change the physical controller mode. While the Addon holds controller authority (MSI Center M disabled), it protects controller ownership so Center M cannot unexpectedly take the controller back.

To hand controller authority back to the stock environment, re-enable MSI Center M from the **Controller** tab. This restarts Windows to apply the change.

## Device tab

The **Device** tab contains global performance settings for the handheld.

Device CPU Boost and TDP Control are independent features and are unaffected by controller presentation.

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

Performance profiles use the actual Steam AppID and operate independently from controller presentation.

## Game detection and Non-Steam games

Steam Addon for Claw does not continuously poll running processes to detect games.

Steam game detection is event-driven. The Addon listens for changes to Steam's `RunningAppID` state and reacts when Steam reports that a game has started, changed, or stopped.

This means the Addon does not repeatedly scan running `.exe` files or periodically ask whether a Steam game is running.

The detected Steam AppID is used for two separate purposes:

- **Controller presentation** — a detected Steam session (or Big Picture) presents the virtual Steam Deck controller instead of the Xbox 360 controller.
- **Performance Profiles** — CPU Boost and TDP profiles use the actual running Steam AppID.

```text
Steam RunningAppID
        │
        ├── Steam game or Big Picture active? ──→ Virtual Steam Deck presentation
        │
        └── Matching enabled Profile? ─────────→ CPU Boost / TDP profile
```

### Non-Steam games

Non-Steam games can use the same presentation and per-game performance features when they are added to the Steam library as a **Non-Steam Game**.

The Profile tab reads Non-Steam shortcuts registered in Steam, so those shortcuts can have their own CPU Boost and TDP profiles just like regular Steam games.

When a Non-Steam game is launched through Steam and Steam reports that shortcut as the current running AppID:

- its enabled CPU Boost / TDP profile can be applied;
- controller presentation switches to the virtual Steam Deck controller.

### Games that use a launcher

Some Non-Steam games first start a separate launcher, which then starts the actual game.

These games are supported only while Steam continues to recognize the Non-Steam shortcut as running after the actual game is launched.

If Steam considers the shortcut finished when the launcher exits, even though the actual game continues running, Steam no longer provides that shortcut as the active `RunningAppID`. In that case, the Addon cannot keep the corresponding Steam Deck presentation or game profile active.

This behavior intentionally follows Steam's own running-game state. The Addon does not scan for the child game process separately or use executable polling as a fallback.

## Steam Quick Access Menu support

Steam Addon for Claw integrates its performance controls into Steam's GamepadUI / Quick Access Menu.

The Addon tab is available when Steam's GamepadUI Quick Access Menu exists, including:

- Steam Big Picture sessions.
- Desktop Steam games using Steam's Big Picture/GamepadUI in-game overlay.

Steam controls whether a Desktop Steam game receives the GamepadUI overlay; the Addon does not
enable that Steam setting automatically.

The Addon tab provides quick access to the same performance settings used by the desktop UI:

- CPU Boost
- TDP PL1 / PL2
- active game Profile controls

When no game is active, the QAM surface exposes Device-level controls. When a supported active game is detected, it presents that game's Profile controls instead.

The QAM integration uses Steam's native GamepadUI components. If a Steam client update changes those internal components in an incompatible way, the Addon disables the affected QAM integration rather than injecting an unsupported fallback UI.

## Automatic updates

Steam Addon for Claw checks for updates automatically when the application starts.

When a new stable release is available:

1. The update is downloaded automatically.
2. Installation is scheduled silently without requiring user interaction.
3. The current process exits and the update is applied.
4. Steam Addon for Claw restarts automatically with the new version.

There is no separate manual update step for normal releases.

If the update service is temporarily unavailable, the update check times out, or the update operation fails, the Addon continues normal startup instead of preventing the application from running.

## Quick start

1. Install the official release package.
2. Launch **Steam Addon for Claw**.
3. Keep Steam running for Steam-related features.
4. Configure optional CPU Boost / TDP defaults in **Device**.
5. Configure game-specific performance settings in **Profile** if desired.
6. Configure the Center M **Normal Action** in **Controller** if desired.
7. Start a Steam game, a Non-Steam game added to Steam, or enter Steam Big Picture Mode — the built-in controller is presented to Steam as a virtual Steam Deck controller automatically.

## Background operation

The controller runtime runs separately from the settings window and remains available from the system tray.

Closing the settings window does not stop controller presentation or profile handling. Use the tray controls when you want to reopen the UI or fully exit the Addon.

## Safety and recovery

The Addon is designed around the normal handheld lifecycle rather than leaving the controller permanently in a routed state.

It handles the controller ownership and recovery path across events such as:

- entering and leaving Steam games and Big Picture Mode
- physical controller re-enumeration
- sleep / hibernate / resume
- application shutdown or restart
- presentation-switch failures and rollback

The Addon does not intentionally replay a stale session after startup. Controller ownership is rebuilt from the current live device state.

## Known limitations

- Physical hardware validation is currently complete on the MSI Claw 8 EX AI+ CG3EM (`MS-1T91`); A2VM models are supported but still awaiting physical-device validation.
- Only the stock MSI Center M controller environment is supported. Handheld Companion, ClawTweaks, and similar controller-management environments are not supported for concurrent use with the Addon.
- Motion / gyro output is not currently supported by the Steam Deck virtual-controller mapping.
- Launcher-based Non-Steam games depend on Steam continuing to report the shortcut as the active `RunningAppID` after the actual game starts.
- QAM integration depends on Steam GamepadUI internals and may require an Addon update after a major Steam client UI change.
- Running another application that independently takes ownership of the same physical controller can conflict with the Addon's controller presentation.

## Troubleshooting

If the virtual Steam Deck controller is not presented during a Steam game:

1. Confirm the device is one of the supported board IDs listed above.
2. Confirm the machine is using the stock MSI Center M controller environment and that Handheld Companion, ClawTweaks, or another controller-management tool is not active.
3. Confirm MSI Center M is disabled in the Controller tab so the Addon holds controller authority.
4. Confirm Steam is running and recognizes the game or Non-Steam shortcut as currently running.
5. Close other controller-management or virtual-controller tools that may be managing the built-in controller.
6. If MSI Center M was opened or the controller was re-enumerated, allow the Addon to return to a stable state and then start the Steam session again.

If a Non-Steam game profile stops applying after a launcher closes, check whether Steam still shows that Non-Steam shortcut as running. The Addon intentionally follows Steam's active AppID rather than scanning the child game executable.

If the Steam Quick Access Addon tab is missing after a Steam client update, the Steam GamepadUI integration may need a compatibility update. Core settings remain available from the desktop UI.

## Development documentation

Technical implementation notes, protocol research, lifecycle design, and the pre-release development README are available in [`docs/`](docs/).

The previous development-oriented README is preserved as [`docs/PRE_RELEASE_DEVELOPMENT_STATUS.md`](docs/PRE_RELEASE_DEVELOPMENT_STATUS.md).

## License

This project is licensed under `AGPL-3.0-only`.
