# Steam Addon for Claw

Steam Addon for Claw adds controller integration and game-specific performance controls for supported MSI Claw handhelds. It can present the built-in controller as a Steam Deck controller while Steam gaming is active, and as an Xbox 360 controller otherwise. You can also configure the controller's front and rear buttons, per-game settings, a performance HUD, shortcuts, a separate Quick Settings Overlay, and Windows gaming-home behavior.

This guide describes the features currently exposed by the app. The Main App includes **Device**, **Controller**, **Steam**, **XBOX**, **ClawHUD**, **Shortcut**, **How to Use**, and **Settings**. **Quick Settings Overlay** is a separate controller-friendly window; it is not Steam Quick Access and it is not the ClawHUD.

## Table of contents

- [Supported devices](#supported-devices)
- [Requirements and compatibility](#requirements-and-compatibility)
- [Quick start](#quick-start)
- [How controller presentation works](#how-controller-presentation-works)
- [MSI Center M and the built-in controller](#msi-center-m-and-the-built-in-controller)
- [Main App guide](#main-app-guide)
  - [Device](#device)
  - [Controller](#controller)
  - [Steam](#steam)
  - [XBOX](#xbox)
  - [ClawHUD](#clawhud)
  - [Shortcut](#shortcut)
  - [Settings](#settings)
- [Game profiles and setting priority](#game-profiles-and-setting-priority)
- [Non-Steam games](#non-steam-games)
- [Quick Settings Overlay](#quick-settings-overlay)
- [Steam Quick Access vs Quick Settings Overlay](#steam-quick-access-vs-quick-settings-overlay)
- [Controller mapping reference](#controller-mapping-reference)
- [Background operation and updates](#background-operation-and-updates)
- [Sleep, resume, and restart](#sleep-resume-and-restart)
- [Recovery and troubleshooting](#recovery-and-troubleshooting)
- [Known limitations](#known-limitations)
- [Return to stock MSI controller behavior and uninstall](#return-to-stock-msi-controller-behavior-and-uninstall)
- [License](#license)

## Supported devices

The app currently recognizes these MSI Claw models:

| Supported device |
| --- |
| MSI Claw 7 AI+ A2VM |
| MSI Claw 8 AI+ A2VM |
| MSI Claw 8 EX AI+ CG3EM |

The app checks that the device is a supported model before starting controller management. If the model cannot be identified or is not supported, controller features will not start.

MSI Claw 8 EX AI+ CG3EM has been tested on physical hardware. The A2VM models are supported by the software, but equivalent physical-device validation is still pending. Hardware-specific controls may also be unavailable on a model that does not expose the required capability.

## Requirements and compatibility

- Windows 11 x64, build 26100 or later.
- One supported MSI Claw model listed above.
- An administrator account for the interactive Windows user. Windows may show a User Account Control (UAC) prompt for operations that need approval; approve it only when you initiated the action.
- Stock MSI Center M installed for the normal MSI controller environment.
- Steam installed and running for Steam Deck controller presentation, Steam game profiles, Steam Big Picture actions, and Steam Quick Access actions.

Do not run another controller-management or virtual-controller-routing application at the same time. Such applications can compete with the Addon and MSI Center M for the built-in controller. The Addon is designed for one interactive Windows user and one session; remote and multi-user sessions are not supported.

## Quick start

1. Download the latest Windows installer from [Releases](https://github.com/onehoon/SteamAddonforClaw/releases/latest) and install it.
2. Approve Windows prompts that appear during installation or first-time component setup. Follow any component-setup instructions shown by the app. If the app asks you to restart Windows, save your work and restart before continuing.
3. Open **Steam Addon for Claw** and check the device status on **Device**.
4. To let the Addon manage the built-in controller, choose **Disable** for **MSI Center M** and confirm **Disable and Restart**. Windows must restart for this controller-authority change to take effect.
5. After Windows starts again, the Addon runs in the background. Open the Main App to configure controls, performance settings, profiles, or ClawHUD.

If you prefer the stock MSI controller behavior, leave MSI Center M enabled or use **Enable and Restart** on the Device page.

## How controller presentation works

When MSI Center M controller authority is disabled and the Addon is managing the controller, the Addon automatically chooses one controller presentation:

| Current use | Controller presented to Windows |
| --- | --- |
| A Steam game is active, or Steam Big Picture is active | Steam Deck-style controller |
| Otherwise | Xbox 360 controller |

There is no separate “Steam Deck mode” switch. Presentation follows Steam game and Big Picture activity. Button behavior configured for **Normal** or **Steam Game / Big Picture** follows the presentation that is active at the time of the press.

## MSI Center M and the built-in controller

The **Device** page contains the **MSI Center M** controls. Disabling MSI Center M hands built-in controller management to the Addon after Windows restarts. Enabling MSI Center M returns controller management to MSI after Windows restarts.

This is a Windows-startup change, not an instant in-session toggle. The confirmation button explicitly says **Disable and Restart** or **Enable and Restart**. Do not power off or close the app part-way through a confirmed transition; let Windows complete the requested restart.

## Main App guide

### Device

The Device page shows the detected manufacturer and model, compatibility status, board identifier, and detected GPU names. Its **MSI Center M** setting controls which app manages the built-in controller; see [MSI Center M and the built-in controller](#msi-center-m-and-the-built-in-controller).

The page also provides device-wide performance and battery controls:

- **TDP Control** — enables processor power-limit management. **PL1** is the sustained power limit and **PL2** is the higher short-duration limit. Values are separate for **Plugged in** and **On battery**. Available ranges depend on the detected Claw model. When available, the first setup may start from the existing MSI Center M Manual TDP values.
- **CPU Boost** — enables management of the Windows processor boost mode, separately for plugged-in and battery use. Choices are **Disabled**, **Enabled**, **Aggressive**, **Efficient Enabled**, **Efficient Aggressive**, **Aggressive At Guaranteed**, and **Efficient Aggressive At Guaranteed**.
- **Windows Power Mode** — enables management of the Windows power mode, separately for each power source: **Best power efficiency**, **Balanced**, or **Best performance**.
- **Battery charge limit** — enables a charging limit from 60% to 100% in 5% steps, when the device supports it.

For **TDP Control**, **CPU Boost**, and **Windows Power Mode**, turning the feature off means the Addon stops managing that setting. It does not necessarily restore the exact Windows or MSI value that existed before the Addon managed it. Your saved choices are kept so you can enable the feature again later.

**Battery charge limit** is different: its switch directly enables or disables the MSI charge-limit function. The selected percentage remains saved for later use.

### Controller

Use **Controller** to choose actions for the **Gamebar Button** (WING) and **Center M Button**. Each has separate **Normal** and **Steam Game / Big Picture** assignments. The current controller presentation selects which assignment is used; the buttons do not have to be remapped when Steam starts or stops.

Available actions depend on the assignment group:

| Action | What it does |
| --- | --- |
| **Quick Settings Overlay** | Opens the Addon's separate Quick Settings Overlay. |
| **Steam Quick Access** | Sends the Steam Deck Quick Access button to Steam. This opens Steam's own Quick Access Menu; it does not open an Addon page inside Steam. Available for Steam Game / Big Picture assignments. |
| **Steam Button** | Sends the Steam system-button input. It is different from Steam Quick Access. Available for Steam Game / Big Picture assignments. |
| **Steam Big Picture** | Opens Steam Big Picture. Available in Normal assignments. |
| **Xbox** | Opens the Xbox app. Available in Normal assignments. |
| **Keyboard/Hotkey** | Sends optional Ctrl, Shift, Alt, or Windows modifiers with one configured key. |
| **Launch Application** | Starts a selected executable, optionally with arguments. |

The first-install defaults are **Gamebar Button: Steam Big Picture** in Normal and **Steam Button** in Steam Game / Big Picture; **Center M Button: Quick Settings Overlay** in both groups. In each mode, **Gamebar Button** and **Center M Button** must use different actions.

The **M1 / M2** section configures the rear buttons while the Addon presents an Xbox 360 controller. Each can be **Disabled** or mapped to A/B/X/Y, D-Pad directions, LB/RB, LT/RT, L3/R3, View, Menu, or Xbox Guide. The same target may be assigned to both. In Steam Game / Big Picture, M1 remains R4 and M2 remains L4; these global Xbox 360 choices do not change that Steam Deck behavior. An enabled per-game XBOX mapping can override the global M1/M2 mapping for that game.

Where supported, **Joystick LED** can be enabled or disabled, with one static color and global brightness from 0% to 100%. **Vibration Strength** adjusts the left and right motors independently from 0% to 100% and includes a **Test** action for each motor. These controls may be hidden or unavailable when the detected device does not support them. Vibration tests act on the controller only while the Addon is managing it.

### Steam

The **Steam** page lists installed Steam games and Non-Steam shortcuts that Steam recognizes. Use **Refresh**, search, and the favorite control to find a game. Select a game to edit its profile. Profiles can be prepared before launching the game; use the profile's enable switch when you want it to apply.

Per-game options include:

- **TDP Control** — separate PL1 and PL2 values for **Plugged in** and **On battery**.
- **CPU Boost** — a Windows processor boost mode for each power source.
- **Windows Power Mode** — a Windows power mode for each power source.
- **Minimum GPU Clock** — a selectable Intel GPU clock floor for each power source, when available. Power or thermal limits can still keep the actual clock below the selected floor.
- **Intel FPS Limit** — a per-power-source target from 40 to 120 FPS in 1 FPS steps. It uses Intel's FPS-limiting support; some games may not support it.
- **Resolution** — **Do not change**, 1920 × 1200, 1920 × 1080, 1680 × 1050, or 1440 × 900.

For **TDP Control**, **CPU Boost**, and **Windows Power Mode**, an enabled profile setting takes priority over the matching Device setting. If one of those profile settings is off, an enabled Device setting can continue to apply. **Minimum GPU Clock**, **Intel FPS Limit**, and **Resolution** are game-profile-only controls and do not have Device-level fallback settings. When the game is no longer active, the profile-only controls stop applying and enabled Device settings for TDP, CPU Boost, and Windows Power Mode become effective again. If a Steam game is launched through a separate launcher and Steam no longer considers its shortcut active after the launcher exits, its profile may stop applying even while a child game process remains open; see [Non-Steam games](#non-steam-games).

### XBOX

The **XBOX** page lists recognized installed Xbox/Microsoft Store games available to the current Windows user. Use **Refresh**, search, and favorites, then select a game to edit its profile. Enable the profile when you want its selected settings to apply while that game is active. Not every Store app or Game Pass title is necessarily recognized by the catalog.

The performance options match the Steam profile options: TDP Control, CPU Boost, Windows Power Mode, Minimum GPU Clock, Intel FPS Limit, and Resolution. The XBOX profile also has a controller section for **Use global M1 / M2 mapping**. Turn that off to choose per-game Xbox 360 targets for M1 and M2. Steam Deck presentation still keeps M1 as R4 and M2 as L4.

As with Steam, you can prepare a profile before the game starts. Its enabled settings apply when the Addon recognizes the corresponding XBOX title as active. For TDP Control, CPU Boost, and Windows Power Mode, an enabled Device setting can continue to apply when the corresponding profile setting is off. Minimum GPU Clock, Intel FPS Limit, and Resolution are game-profile-only and have no Device-level fallback.

### ClawHUD

**ClawHUD** is the performance HUD included and managed by the Addon. You do not need to install the standalone ClawHUD app. Enable the HUD, then choose **In game only** or **Always** for its display mode.

Depending on device and data availability, the HUD can show **FPS**, **CPU usage and temperature**, **GPU usage and clock**, **CPU package power (TDP)**, **RAM usage**, **VRAM usage**, **fan speed**, and **battery information**. While running on battery, battery information can include charge percentage and estimated remaining time. Unavailable measurements are omitted rather than treated as a real zero.

With Intel XeFG, the FPS value may not include every driver-generated output frame because PresentMon may not observe all of those generated frames.

The page lets you adjust **Size** (**-2**, **-1**, **Default**, **+1**, **+2**), **Font** (**Unispace** or **Segoe UI Variable**), **Alignment** (**Left**, **Center**, **Right**), and **Background width** (**Full width** or **Content width**). **Opacity** ranges from 50% to 100% in 5% steps. **Intel VRR Range Fix** is a device/display-specific option for correcting a variable-refresh-rate range when applicable; it is not needed on every display. If the HUD reports a problem, use the page's **Retry** action when available.

### Shortcut

Use **Shortcut** to add, edit, delete, and drag to reorder items shown in Quick Settings Overlay. Supported actions are:

- Launch an application, with optional arguments.
- Run a PowerShell script.
- Open a website URL.
- Capture the primary display as a JPEG screenshot.
- Open Steam Big Picture, the Steam client, or the Xbox app.

For screenshots, choose a custom save folder or use the default Windows `Pictures\Screenshots` folder. **Browse** selects a folder, **Open folder** opens it in File Explorer, and **Use default** returns to the default location. A shortcut displayed in the Overlay runs the action you configured here; edit its details in the Main App.

### Settings

#### Application updates

Choose **Check** to check again and download an available update. When a downloaded update is ready, the button changes to **Install update**. Selecting it restarts the Addon so the update can be applied at the next safe startup. If no update is available, the app reports that it is up to date.

#### Windows Gaming Full Screen Experience

Select **Off**, **Xbox**, or **Steam Big Picture** as the Windows gaming home. **Off** disables the Addon-selected gaming home. **Start on Windows startup** controls whether Windows starts directly in the selected gaming home. If Windows is already set to another app, **Other Windows app** shows that current selection; it cannot be selected as a new Addon-managed option, and the Addon does not manage its startup behavior.

#### Show only current power source

When enabled, Quick Settings Overlay shows controls for the currently connected power source. The Main App continues to show both **Plugged in** and **On battery** values.

#### Required Components

View whether components needed by controller features are ready. This is a status area, not a manual driver-install panel. Follow any setup or restart prompt shown by the app.

#### Developer Menu

Developer Menu is reserved for developer diagnostics and is not needed for normal setup or use.

## Game profiles and setting priority

You can create and edit Steam and XBOX game profiles before starting a game. Saved values are kept when a profile is disabled.

**TDP Control**, **CPU Boost**, and **Windows Power Mode** exist at both Device and game-profile level. When one of these settings is enabled in the active game profile, the profile value takes priority. If that profile setting is off, the enabled Device setting can continue to apply.

**Minimum GPU Clock**, **Intel FPS Limit**, and **Resolution** are game-profile-only controls. They do not fall back to a Device setting when disabled or when no matching game profile is active. **Battery charge limit** is Device-only and is not part of a game profile.

```text
Recognized active game + enabled profile
    -> enabled TDP / CPU Boost / Power Mode profile settings override Device
    -> enabled GPU Clock / FPS Limit / Resolution profile settings apply for that game
    -> disabled TDP / CPU Boost / Power Mode profile settings can use enabled Device settings

No active matching profile
    -> enabled Device settings continue for Device-level features
    -> no game-profile-only setting is active
```

When the game stops being active, the profile-only settings stop applying and enabled Device settings become effective again where a Device-level counterpart exists. Turning TDP Control, CPU Boost, or Windows Power Mode off at Device level stops the Addon from managing that Device setting; it does not promise to restore a previous Windows or MSI value. Battery charge limit uses its own direct enable/disable behavior described in the Device section.

## Non-Steam games

To use a Steam profile for a regular Windows game, first add that game to Steam as a **Non-Steam Game**. The **Steam** page lists shortcuts that Steam recognizes, so you can prepare and enable a profile there. Start the game through its Steam shortcut and confirm Steam continues to show it as active. Some launchers close their own shortcut when they start a separate game process; if Steam then stops recognizing the shortcut, the Addon may stop applying that profile too.

## Quick Settings Overlay

Quick Settings Overlay is the Addon's separate controller-friendly settings window. It uses the same saved settings as the Main App. Its tabs are **Device**, **Profile**, **Controller**, **Shortcut**, and **Setting**; you can change their order from **Setting → Tab Order**.

- **Device** — quick access to supported device-wide TDP Control, CPU Boost, Windows Power Mode, and battery charge-limit settings.
- **Profile** — settings for the currently active recognized game. When no game is active, the page says: “No game is currently running. Start a game to configure its profile.” The available rows depend on the game and device; XBOX profiles can also expose M1/M2 mapping.
- **Controller** — global Xbox 360 M1/M2 mapping and, when supported, joystick LED and vibration settings.
- **Shortcut** — run your configured shortcuts. Add or edit shortcut details in the Main App.
- **Setting** — rearrange the Overlay tabs and choose whether to show only the current power source.

Controller navigation uses **LB / RB** to move between tabs, wrapping from the last tab to the first and vice versa. Use **Up / Down** to move through page rows, **Left / Right** to adjust the selected value or move within the shortcut grid, **A** to activate the selected control or shortcut, and **B** to go back or close the Overlay.

## Steam Quick Access vs Quick Settings Overlay

These are separate interfaces:

| Name | Owner | What opens |
| --- | --- | --- |
| **Steam Quick Access Menu** (Steam Quick Access) | Steam | Steam's native Quick Access interface. The Controller action sends Steam the Quick Access button. |
| **Quick Settings Overlay** | Steam Addon for Claw | The Addon's own Device, Profile, Controller, Shortcut, and Setting tabs. |
| **ClawHUD** | Steam Addon for Claw / included ClawHUD runtime | A performance display over the screen. It is not a settings menu. |
| **Steam Button** | Steam | The Steam system-button input. It is not the Quick Access command and does not open the Addon's Overlay. |

The Addon does not place its own settings tab inside Steam's Quick Access Menu.

## Controller mapping reference

The built-in controller's face buttons, D-Pad, bumpers, triggers, sticks, and stick clicks are passed through as their corresponding standard gamepad controls. The current presentation changes the type of controller Windows sees, not the basic meaning of those controls.

| Built-in control | Steam Deck-style presentation | Xbox 360 presentation |
| --- | --- | --- |
| A / B / X / Y, D-Pad | Corresponding controls | Corresponding controls |
| LB / RB | L1 / R1 | LB / RB |
| LT / RT analog travel | L2 / R2 analog triggers | LT / RT analog triggers |
| LT / RT fully pressed | L2 / R2 digital full pull | No separate digital full-pull output; XInput uses the analog LT / RT values |
| Left/right sticks and L3 / R3 | Corresponding sticks and clicks | Corresponding sticks and clicks |
| View / Back | View / Options function | View |
| Menu / Start | Menu function | Menu / Start |
| M1 / M2 | R4 / L4 | Configurable in Controller; Disabled by default |

WING and Center M are configurable front buttons; they are not part of the ordinary gamepad mapping table. The app does not currently expose gyro or motion output as a user-configurable controller feature.

## Background operation and updates

Closing the Main App window does not stop the background Addon or controller presentation. The notification-area icon provides **Open** and **Restart Addon**. Use **Open** to bring back the Main App; use **Restart Addon** if you need to restart the running Addon. The tray menu does not provide a general **Quit** command. To return controller management to MSI Center M, use **Device → MSI Center M → Enable and Restart**; use the normal Windows uninstall path to remove the app.

The Addon checks for and downloads updates in the background after startup. This does not interrupt the current session. The downloaded update is applied on a later safe Addon startup. You can also use **Settings → Application updates → Check** to check and download now, then choose **Install update** when it becomes available. If checking or downloading fails, the Addon continues running; try **Check** again later.

## Sleep, resume, and restart

The Addon is designed to recover its controller presentation and settings after Windows sleep, hibernate, resume, and restart. After waking, Windows may take a short time to reconnect the built-in controller. Wait for the device to settle before changing controller ownership or starting another controller utility.

If the controller has not returned after a short wait, try **Restart Addon** from the notification-area icon. If you want to stop Addon controller management, use **Device → MSI Center M → Enable and Restart** and let Windows restart.

## Recovery and troubleshooting

### A Steam game appears as an Xbox controller

Confirm that Steam is running and that Steam recognizes the game as active. Confirm that MSI Center M is disabled for Addon controller ownership. Steam Deck presentation follows Steam game and Big Picture activity; it is not selected manually.

### The controller does not respond after sleep or resume

Wait briefly for Windows to reconnect the controller, then use **Restart Addon** from the tray icon if needed. If the Addon still cannot manage the controller, return to MSI Center M with **Enable and Restart**. Do not manually remove controller devices or change driver settings as a first troubleshooting step.

### WING or Center M performs the wrong action

Open **Controller** and check the button's action under both **Normal** and **Steam Game / Big Picture**. The selected group follows the active controller presentation. Check that another controller utility is not also intercepting the button.

### Quick Settings Overlay does not open

Check that the button you are pressing is assigned to **Quick Settings Overlay** for the current presentation group. Confirm that the Addon is running (the tray icon is present) and currently owns the controller. You can also restart the Addon from the tray.

### A game profile does not apply

In **Steam** or **XBOX**, select the correct game and confirm that its profile and the specific feature you want are enabled. For Steam, confirm Steam still recognizes the game as active. Check whether the setting is for **Plugged in** or **On battery**. TDP Control, CPU Boost, and Windows Power Mode override their Device counterparts only when enabled in the active profile. Minimum GPU Clock, Intel FPS Limit, and Resolution are profile-only settings and apply only when their profile controls are enabled.

### A Non-Steam profile stops applying after a launcher closes

Steam may stop recognizing the shortcut as active when a separate launcher exits, even if the launcher started another game. Addon game profiles follow the game activity Steam reports. Add or launch the game in a way that keeps its Steam shortcut active.

### ClawHUD is not visible

Open **ClawHUD**, enable **Enable HUD**, and check **Display mode**. If the status reports a problem, use **Retry** when available. Some metrics or display-specific options may not be available on every device.

### An XBOX game is missing

Use **Refresh** and search again. The catalog contains recognized installed Xbox/Microsoft Store games for the current Windows user; it does not guarantee that every Store app or subscription title will appear.

### Required components are not ready

Open **Settings → Required Components** to see the reported status. Follow the in-app setup instructions and restart Windows if requested. If the status remains unavailable, use **Restart Addon** and check again. Do not install or remove controller drivers manually to work around the status.

## Known limitations

- Only the MSI Claw models listed in [Supported devices](#supported-devices) are recognized.
- Some settings depend on hardware, Windows, Intel graphics support, or game support and may be unavailable on a particular device or title.
- Intel FPS limiting is not supported by every game. Minimum GPU Clock is available only when the graphics driver exposes selectable values; actual clocks remain subject to power and thermal limits.
- The XBOX catalog includes recognized installed titles, not every app in Microsoft Store or Game Pass.
- Steam profile tracking depends on Steam continuing to report the game or shortcut as active. Some launcher chains can cause a profile to stop applying when Steam loses that association.
- Quick Settings Overlay is separate from Steam Quick Access. The Addon does not inject an Addon tab into Steam's menu.
- Gyro/motion mapping and a general Device-page Fan Control are not currently exposed as user features.
- The product supports one interactive Windows user and session; Fast User Switching, Remote Desktop sessions, and multi-user use are not supported.

## Return to stock MSI controller behavior and uninstall

To return controller management to MSI Center M, open **Device**, choose **Enable**, and confirm **Enable and Restart**. Let Windows restart before using the stock controller environment.

To remove the app, use its normal entry in **Windows Settings → Apps → Installed apps** and choose **Uninstall**. The uninstaller prepares a safe controller handoff; if it reports that preparation did not complete, do not force-remove controller components—resolve the reported issue and retry. Your Addon settings, game profiles, shortcuts, and logs are retained after uninstall so they are available if you reinstall. The managed ClawHUD runtime is removed with the app.

## License

Steam Addon for Claw is distributed under the license in [`LICENSE`](LICENSE). Third-party components remain subject to their own licenses and notices included with the installation.
