# Steam Addon for Claw

Steam Addon for Claw adds controller integration and game-specific performance controls for supported MSI Claw handhelds. It can present the built-in controller as a Steam Deck or DualSense Edge while Steam gaming is active, and as an Xbox 360 controller otherwise. You can choose a global Steam controller and an optional per-game Steam override, configure the controller's front and rear buttons, set per-game performance options, and use a performance HUD, shortcuts, a separate Quick Settings Overlay, and Windows gaming-home behavior.

This guide describes the features currently exposed by the app. The Main App includes **Device**, **MSI Update**, **Controller**, **Steam**, **XBOX**, **ClawHUD**, **Quick Overlay**, **Shortcut**, **Tweaks**, **How to Use**, and **Settings**. **Quick Settings Overlay** is a separate controller-friendly window; it is not Steam Quick Access and it is not the ClawHUD.

## Table of contents

- [Supported devices](#supported-devices)
- [Requirements and compatibility](#requirements-and-compatibility)
- [Quick start](#quick-start)
- [How controller presentation works](#how-controller-presentation-works)
- [Gyroscope / IMU — Steam Input configuration](#gyroscope--imu--steam-input-configuration)
- [MSI Center M and the built-in controller](#msi-center-m-and-the-built-in-controller)
- [Main App guide](#main-app-guide)
  - [Device](#device)
  - [MSI Update](#msi-update)
  - [Controller](#controller)
  - [Steam](#steam)
  - [XBOX](#xbox)
  - [ClawHUD](#clawhud)
  - [Quick Overlay](#quick-overlay)
  - [Shortcut](#shortcut)
  - [Tweaks](#tweaks)
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

All three listed MSI Claw models, including **MSI Claw 7 AI+ A2VM** and **MSI Claw 8 AI+ A2VM**, have been tested on physical hardware. The supported A2VM 7/8 device features have completed hardware testing. Availability of LED, vibration, motion, and other model-specific controls still depends on the detected device and firmware; game compatibility can vary.

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

If the controller's motors stop vibrating, open **Controller → Vibration Strength → Vibration Recovery** and choose **Restore** after saving and closing your game. Automatic vibration recovery is not enabled by default. Use the **Left Motor** and **Right Motor** Test buttons afterward to check the motors.

If you prefer the stock MSI controller behavior, leave MSI Center M enabled or use **Enable and Restart** on the Device page.

## How controller presentation works

When MSI Center M controller authority is disabled and the Addon is managing the controller, the Addon automatically chooses one controller presentation:

Supported **virtual controller presentations** are:

| Virtual controller | When it is used |
| --- | --- |
| **Xbox 360** | Normal Windows use, outside an active Steam game or Big Picture |
| **Steam Deck** | Steam game or Big Picture, when selected by the global preference or an enabled game profile |
| **DualSense Edge** | Steam game or Big Picture, when selected in **Controller → Controller for Steam Game** or an enabled Steam game profile |

Choose the global **Steam Deck** or **DualSense Edge** option in **Controller → Controller for Steam Game**. Steam Deck is the default. An enabled Steam game profile can select its own controller or **Use default** to inherit the global choice. The controller selector is locked while a Steam game or Big Picture Mode is active and shows the controller actually applied, including a game-profile override. Selection is available when neither Steam nor Big Picture is active, including normal Windows or Xbox-only gameplay. The separate **Gyroscope** switch remains available in every state. Steam game launch and exit still switch automatically to an enabled game's override and back to the global choice when Big Picture remains active. Outside Steam game and Big Picture activity, Windows continues to use Xbox 360. Only one virtual controller presentation is attached at a time. Button behavior configured for **Normal** or **Steam Game / Big Picture** follows the active presentation.

**Steam Input must be enabled for the supported Addon configuration.** When using the Addon's **Steam Deck** or **DualSense Edge** virtual controller in a Steam game, open the game's **Properties → Controller → Override for this game → Enable Steam Input**. Keep Steam Input **ON** even if the game has its own native DualSense support. Some games may still produce controller input or rumble with Steam Input disabled, but that is not the officially supported Addon configuration.

**DualSense Edge vibration scope:** The Addon forwards only ordinary two-motor **rumble** to the MSI Claw physical motors. Sony **L2/R2 adaptive-trigger resistance or vibration** and **USB-audio advanced haptics** are **not** translated to Claw motors. A vibration felt while pressing R2 may come from a separate ordinary rumble command; it does not indicate that adaptive-trigger haptics have been reproduced.

## Gyroscope / IMU — Steam Input configuration

The global **Gyroscope** switch is off by default on a new installation; an existing saved choice is preserved. When on and the Addon is presenting a Steam-facing virtual controller, available physical **gyroscope and accelerometer** data is passed to Steam Input. Turning it off stops motion-sensor collection and sends no motion input; ordinary controller input and the virtual-device connection remain active. The Addon does **not** perform gyro-to-mouse or gyro-to-joystick conversion itself, and its **Xbox 360 presentation does not provide gyro input**.

DualSense Edge presentation also forwards available motion data through its existing publisher path. Edge button, PS/Quick Access, rumble, and motion behavior have not been comprehensively verified across every supported device and game.

**If gyro is a priority, consider DualSense Edge.** On the tested MSI Claw 8 AI+ A2VM, gyro felt smoother in DualSense Edge mode, while **Gyro To Mouse** in Steam Deck mode was reported to be choppy. This can vary by game and device. The Gyroscope toggle enables motion input; gyro activation and sensitivity are configured in Steam Input.

On that same device, Steam's gyro calibration showed live motion and gyro-to-joystick camera control worked in a game when Steam Input's activation was set to **Always On**.

**Configure and fine-tune gyro in Steam Input, not in the Addon.** Open the game's **Controller Layout** in Steam, configure **Gyro Behavior** (such as gyro-to-mouse or gyro-to-joystick), and adjust the **activation button/condition**, sensitivity, and other motion settings there. In particular, a gyro activation condition can prevent in-game movement **even when the gyro calibration screen shows live values**. For a quick test, select **Always On**; afterward, set your preferred activation button and sensitivity in Steam Input.

Gyro input has been verified on **both A2VM 7-inch and A2VM 8-inch** hardware. Gyro behavior on EX/CG3EM hardware has not yet been fully verified.

## MSI Center M and the built-in controller

The **Device** page contains the **MSI Center M** controls. Disabling MSI Center M hands built-in controller management to the Addon after Windows restarts. Enabling MSI Center M returns controller management to MSI after Windows restarts.

This is a Windows-startup change, not an instant in-session toggle. The confirmation button explicitly says **Disable and Restart** or **Enable and Restart**. Do not power off or close the app part-way through a confirmed transition; let Windows complete the requested restart.

## Main App guide

### Device

The Device page shows the detected manufacturer and model, compatibility status, board identifier, and detected GPU names. Its **MSI Center M** setting controls which app manages the built-in controller; see [MSI Center M and the built-in controller](#msi-center-m-and-the-built-in-controller).

The page also provides device-wide performance and battery controls:

- **TDP Control** — enables processor power-limit management. **PL1** is the sustained power limit and **PL2** is the higher short-duration limit. Values are separate for **Plugged in** and **On battery**. Available ranges depend on the detected Claw model. When available, the first setup may start from the existing MSI Center M Manual TDP values.
- **CPU Boost** — enables management of the Windows processor boost mode, separately for plugged-in and battery use. Choices are **Disabled**, **Enabled**, **Aggressive**, **Efficient Enabled**, **Efficient Aggressive**, **Aggressive At Guaranteed**, and **Efficient Aggressive At Guaranteed**.
- **Maximum Processor State** — optionally sets the Windows maximum processor performance state to **99%** or **100%** separately for AC and DC. It is off for new users, is not a CPU-utilization limit, and is independent of CPU Boost. Turning it off stops the Addon from applying the setting; it does not restore the previous Windows value.
- **Windows Power Mode** — enables management of the Windows power mode, separately for each power source: **Best power efficiency**, **Balanced**, or **Best performance**.
- **Battery charge limit** — enables a charging limit from 60% to 100% in 5% steps, when the device supports it.

For **TDP Control**, **CPU Boost**, **Maximum Processor State**, and **Windows Power Mode**, turning the feature off means the Addon stops managing that setting. It does not necessarily restore the exact Windows or MSI value that existed before the Addon managed it. Your saved choices are kept so you can enable the feature again later.

**Battery charge limit** is different: its switch directly enables or disables the MSI charge-limit function. The selected percentage remains saved for later use.

### MSI Update

The **MSI Update** page can manually query MSI's published catalog for BIOS, firmware, drivers, and utilities and Intel DSA's driver catalog for hardware-ID-matched Intel graphics, Wi-Fi, Bluetooth, and NPU entries. It also reads locally available BIOS, PnP driver, software, package, and controller-revision information and labels the source and confidence for each displayed version. Where the catalog version and installed identity are safely comparable, the page reports **Update available** or **Up to date**; uncertain or unmatched items remain **Unknown**. The page does not download or install anything. The MSI query uses the original MSI Live Update component when available; it does not start MSI Center M or its services. Intel DSA is queried only when the user presses **Check for updates**; source failures are reported independently.

### Controller

At the top of **Controller**, use **Controller for Steam Game** to choose **Steam Deck** (the default) or **DualSense Edge** and toggle **Gyroscope**. The selection is used by Big Picture and by Steam games whose enabled profile uses **Use default**. Controller selection is locked during a Steam game or Big Picture and displays the actually applied controller; Gyroscope remains changeable. Both controls are available in normal Windows or Xbox-only gameplay when the app can verify their settings.

Use **Controller** to choose actions for the **XBOX Gamebar Button** (WING) and **Center M Button**. Each has separate **Normal** and **Steam Game / Big Picture** assignments. The current controller presentation selects which assignment is used; the buttons do not have to be remapped when Steam starts or stops.

Available actions depend on the assignment group:

| Action | What it does |
| --- | --- |
| **Quick Settings Overlay** | Opens the Addon's separate Quick Settings Overlay. |
| **Steam Quick Access** | Sends the Steam Deck Quick Access button to Steam. This opens Steam's own Quick Access Menu; it does not open an Addon page inside Steam. Available for Steam Game / Big Picture assignments. |
| **Steam Button** | Sends the Steam system-button input. It is different from Steam Quick Access. Available for Steam Game / Big Picture assignments. |
| **Steam Big Picture** | Opens Steam Big Picture. Available in Normal assignments. |
| **Xbox** | Opens the Xbox app. Available in Normal assignments. |
| **Keyboard/Hotkey** | Sends optional Ctrl, Shift, Alt, or Windows modifiers with one configured key. |
| **Launch Application** | Starts a selected executable, optionally with arguments. Turn on **Run as administrator** only if the app needs elevated permissions. |

The first-install defaults are **Gamebar Button: Steam Big Picture** in Normal and **Steam Button** in Steam Game / Big Picture; **Center M Button: Quick Settings Overlay** in both groups. In each mode, **Gamebar Button** and **Center M Button** must use different actions.

The **M1 / M2** section configures the rear buttons while the Addon presents an Xbox 360 controller. Each can be **Disabled** or mapped to A/B/X/Y, D-Pad directions, LB/RB, LT/RT, L3/R3, View, Menu, or Xbox Guide. The same target may be assigned to both. In Steam Game / Big Picture, M1 remains R4 and M2 remains L4 for both Steam Deck and DualSense Edge; the global Xbox 360 mappings do not change that behavior. An enabled per-game XBOX mapping can override the global M1/M2 mapping for that game.

Where supported, **Joystick LED** can be enabled or disabled, with one static color and brightness from 0% to 100%. **Vibration Strength** adjusts the left and right motors independently from 0% to 100%; use each motor's **Test** button to check the result. These options may be unavailable when the detected model or firmware does not support them; both A2VM models have been tested on hardware.

The **Vibration Strength** section also includes **Vibration Recovery → Restore** for all three supported models. It temporarily disconnects the virtual controller, so save and close your game first. A success message does not guarantee that the motors are working; use both **Test** buttons afterward. Automatic vibration recovery is not enabled by default.

### Steam

The **Steam** page lists installed Steam games and Non-Steam shortcuts that Steam recognizes. Use **Refresh**, search, and the favorite control to find a game. Select a game to edit its profile. Profiles can be prepared before launching the game; use the profile's enable switch when you want it to apply.

The first profile card is **Virtual Controller**. Choose **Use default**, **Steam Deck**, or **DualSense Edge**. The choice can be edited only while the profile is enabled and Steam / Big Picture is idle; turning the profile off retains its saved choice. With Big Picture active, a game's enabled override is selected automatically when Steam reports that game as active, then the global choice is restored when the game ends and Big Picture remains active. **Use default** always inherits **Controller → Controller for Steam Game**.

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

### Quick Overlay

Use **Quick Overlay** to configure the separate Quick Settings Overlay. **Y Button Quick Jump** chooses the tab selected by pressing Y while the Overlay is open; it defaults to **Profile** and can be set to **Off**. This destination is independent of tab order: **Quick Overlay Tab Order** controls which tab opens first on the next Overlay Show, while Y always jumps to its configured destination. The in-game **Setting → Quick Settings** page provides the same Y choice and tab-order controls. **Show only current power source** limits the Overlay to controls for the currently connected power source. The Main App continues to show both **Plugged in** and **On battery** values. Expand **Quick Overlay Tab Order** to move tabs one position at a time with the Up and Down buttons.

### Shortcut

Use **Shortcut** to add, edit, delete, and drag to reorder items shown in Quick Settings Overlay. Supported actions are:

- Launch an application, with optional arguments.
- Run a PowerShell script.
- Open a website URL.
- Capture the primary display as a JPEG screenshot.
- Open Steam Big Picture, the Steam client, or the Xbox app.
- Show or hide the Windows touch keyboard, toggle Show Desktop, or open the controller layout for the currently running Steam game.
- Turn the ClawHUD performance overlay on or off.

For **Application (.exe)** and **PowerShell** shortcuts, **Run as administrator** is off by default. Turn it on only when the action needs administrator privileges. Other shortcut types have no administrator option.

The Screenshot action captures the primary display as a JPEG. Choose a custom save folder with **Browse**, or use the default Windows `Pictures\Screenshots` folder with **Use default**. **Open folder** opens the selected location. Click **Save** to keep your changes; **Cancel** discards them. You can configure one Screenshot shortcut.

Shortcut tiles have an icon appropriate to their action. In the Main App's Add/Edit dialog, you can choose another icon or restore the default. For non-screenshot shortcuts, **Close Overlay after launch** lets you choose whether the Overlay closes after the action runs.

### Tweaks

The **Tweaks** page lets you change supported Windows features and registry settings. Review the safety notice before using it; it can appear once per day. Some changes require a Windows or Explorer restart, which the Addon does not perform automatically. The app shows when a change is pending, unsupported, or controlled by Windows policy.

Under **Gaming Performance**:
- **Memory Integrity** — turns Windows kernel memory protection on or off. **Turning it off reduces security.** Turning it on can affect incompatible drivers or applications.
- **Virtual Machine Platform** — turns the Windows virtualization feature on or off. Disabling it may prevent virtualization-dependent software from working.

Under **Registry Tweaks**:
- **Faster Menus** — reduces submenu delay and disables menu animation.
- **Classic Context Menu** — uses the classic Windows 11 right-click menu.
- **Disable Password on Wake** — skips sign-in after sleep. **Anyone with physical access could open your desktop.**
- **Disable Edge Background** — disables Edge Startup Boost and background mode; already-running Edge processes may remain until closed.
- **Disable Startup App Delay** — removes configured delays for startup apps; startup improvements vary.

For settings named **Disable ...**, switching the option **on** applies the disabling behavior described by its name. Turning it off reverses that option where Windows allows it.

### Settings

#### Application updates

Choose **Check** to check again and download an available update. When a downloaded update is ready, the button changes to **Install update**. Selecting it restarts the Addon so the update can be applied at the next safe startup. If no update is available, the app reports that it is up to date.

#### XBOX Mode (Full Screen Experience)

Select **Off**, **XBOX**, or **Steam Big Picture** as the Windows gaming home. **Off** disables the Addon-selected gaming home. **Start on Windows startup** controls whether Windows starts directly in the selected gaming home. If Windows is already set to another app, **Other Windows app** shows that current selection; it cannot be selected as a new Addon-managed option, and the Addon does not manage its startup behavior.

#### App theme

Choose **System**, **Light**, or **Dark** for the Main App's appearance.

#### Logging

Choose the log detail level (**Off**, **Info**, or **Debug**). **Open folder** opens the folder containing app logs, which may be useful when reporting a problem.

#### Required Components

View whether components needed by controller features are ready. This is a status area, not a manual driver-install panel. Follow any setup or restart prompt shown by the app.

## Game profiles and setting priority

You can create and edit Steam and XBOX game profiles before starting a game. Saved values are kept when a profile is disabled.

A Steam game's **Virtual Controller** choice is available only while its master profile is enabled. **Use default** or a disabled/missing profile inherits the global **Controller → Controller for Steam Game** choice. When a game starts in Big Picture, its enabled explicit choice is selected automatically; when it ends and Big Picture stays open, the global choice resumes. Manual global and per-game controller choices are locked while any Steam game or Big Picture Mode is active. Turning a profile off keeps its saved controller choice for when the profile is enabled again.

**TDP Control**, **CPU Boost**, and **Windows Power Mode** exist at both Device and game-profile level. When one of these settings is enabled in the active game profile, the profile value takes priority. If that profile setting is off, the enabled Device setting can continue to apply.

**Minimum GPU Clock**, **Intel FPS Limit**, and **Resolution** are game-profile-only controls. They do not fall back to a Device setting when disabled or when no matching game profile is active. **Battery charge limit** and **Maximum Processor State** are Device-only and are not part of a game profile.

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

Quick Settings Overlay is the Addon's separate controller-friendly settings window. It uses the same saved settings as the Main App. Its default tab order is **Shortcut**, **Device**, **Controller**, **Setting**, and **Profile**; you can change their order from **Setting → Tab Order**.

- **Device** — quick access to supported device-wide TDP Control, CPU Boost, Windows Power Mode, and battery charge-limit settings.
- **Profile** — settings for the currently active recognized game. When no game is active, the page says: “No game is currently running. Start a game to configure its profile.” The available rows depend on the game and device; XBOX profiles can also expose M1/M2 mapping.
- **Controller** — the shared **Controller for Steam Game** selection and **Gyroscope** toggle, Xbox 360 M1/M2 mapping, and (when supported) joystick LED and vibration settings.
- **Shortcut** — run your configured shortcuts. Add or edit shortcut details in the Main App.
- **Setting** — rearrange the Overlay tabs and choose whether to show only the current power source.

Controller navigation uses **LB / RB** to move between tabs, wrapping from the last tab to the first and vice versa. Use **Up / Down** to move through page rows, **Left / Right** to adjust the selected value or move within the shortcut grid, **A** to activate the selected control or shortcut, and **B** to go back or close the Overlay.

## Steam Quick Access vs Quick Settings Overlay

These are separate interfaces:

| Name | Owner | What opens |
| --- | --- | --- |
| **Steam Quick Access Menu** (Steam Quick Access) | Steam | Steam's native Quick Access interface. The Controller action sends Steam the Quick Access button. |
| **Quick Settings Overlay** | Steam Addon for Claw | The Addon's own Device, Profile, Controller, Shortcut, and Setting tabs. |
| **ClawHUD** | Steam Addon for Claw | A performance display over the screen. It is not a settings menu. |
| **Steam Button** | Steam | The Steam system-button input. It is not the Quick Access command and does not open the Addon's Overlay. |

The Addon does not place its own settings tab inside Steam's Quick Access Menu.

## Controller mapping reference

The built-in controller's face buttons, D-Pad, bumpers, triggers, sticks, and stick clicks are passed through as their corresponding standard gamepad controls. The current presentation changes the type of controller Windows sees, not the basic meaning of those controls.

| Built-in control | Steam Deck | DualSense Edge | Xbox 360 |
| --- | --- | --- | --- |
| A / B / X / Y | Corresponding buttons | Cross / Circle / Square / Triangle | A / B / X / Y |
| D-Pad | Corresponding directions | Corresponding directions | Corresponding directions |
| LB / RB | L1 / R1 | L1 / R1 | LB / RB |
| LT / RT analog travel | L2 / R2 analog triggers | L2 / R2 analog triggers | LT / RT analog triggers |
| LT / RT fully pressed | L2 / R2 digital full pull | L2 / R2 digital press | No separate digital full-pull output |
| Left/right sticks and L3 / R3 | Corresponding sticks and clicks | Corresponding sticks and clicks | Corresponding sticks and clicks |
| View / Back | View / Options function | Create | View |
| Menu / Start | Menu function | Options | Menu / Start |
| M1 / M2 | R4 / L4 | R4 / L4 | Configurable in Controller; Disabled by default |

WING and Center M are configurable front buttons; they are not part of the ordinary gamepad mapping table. Steam Deck and DualSense Edge presentations can forward available gyro and accelerometer data to Steam Input. **Configure activation, gyro-to-mouse/joystick behavior and sensitivity in Steam Input**, not in the Addon. See [Gyroscope / IMU](#gyroscope--imu--steam-input-configuration).

## Background operation and updates

Closing the Main App window does not stop the background Addon or controller presentation. The notification-area icon provides **Open** and **Restart Addon**. Use **Open** to bring back the Main App; use **Restart Addon** if you need to restart the running Addon. The tray menu does not provide a general **Quit** command. To return controller management to MSI Center M, use **Device → MSI Center M → Enable and Restart**; use the normal Windows uninstall path to remove the app.

The Addon checks for and downloads updates in the background after startup without interrupting your current session. Downloaded updates are applied on a later safe Addon startup. You can also select **Settings → Application updates → Check** to check manually, then **Install update** when it is ready. If checking or downloading fails, the Addon continues running; try **Check** again later.

## Sleep, resume, and restart

The Addon is designed to recover its controller and settings after Windows sleep, hibernate, resume, and restart. After waking, Windows may take a short time to reconnect the built-in controller. Wait for the device to settle before changing controller ownership or starting another controller utility.

If the controller has not returned after a short wait, try **Restart Addon** from the notification-area icon. If you want to stop Addon controller management, use **Device → MSI Center M → Enable and Restart** and let Windows restart.

## Recovery and troubleshooting

### A Steam game appears as an Xbox controller

Confirm that Steam is running and that Steam recognizes the game as active. Confirm that MSI Center M is disabled for Addon controller ownership. Set the global choice at **Controller → Controller for Steam Game** before starting a game, or enable the Steam profile and choose its **Virtual Controller** override. Manual changes are disabled while a Steam game or Big Picture Mode is active.

### Gyro values appear in Steam calibration, but the game does not respond

Check the game's **Steam Input Controller Layout → Gyro Behavior** and, especially, its separate **gyro activation button/condition**. Test with **Always On** first, then adjust your preferred activation button and sensitivity **in Steam Input**. The Addon does not provide an independent gyro activation or fine-tuning setting.

### The controller does not respond after sleep or resume

Wait briefly for Windows to reconnect the controller, then use **Restart Addon** from the tray icon if needed. If the Addon still cannot manage the controller, return to MSI Center M with **Enable and Restart**. Do not manually remove controller devices or change driver settings as a first troubleshooting step.

### Motors do not vibrate

While the Addon manages the controller, use **Controller → Vibration Strength** and run **Left Test** and **Right Test**. If the motors remain silent, save your game and close it, then choose **Vibration Recovery → Restore** and retry both tests. The action temporarily disconnects the virtual controller. A success message does not confirm the motors are working. If recovery fails, try **Device → MSI Center M → Enable and Restart** to return controller management to MSI when available.

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
- **Steam Deck and DualSense Edge** support motion input. On a tested A2VM 8, Steam Deck Gyro To Mouse could feel choppy; DualSense Edge was smoother in that comparison. Results may differ by game and device. Configure gyro activation and sensitivity in **Steam Input**.
- Joystick LED and vibration features depend on the device and firmware. The supported A2VM 7/8 functionality has been tested on physical hardware. Automatic vibration recovery is disabled by default. After a manual **Restore**, use the motor **Test** buttons to confirm vibration.
- A general Device-page Fan Control is not currently available as a user feature.
- The product supports one interactive Windows user and session; Fast User Switching, Remote Desktop sessions, and multi-user use are not supported.

## Return to stock MSI controller behavior and uninstall

To return controller management to MSI Center M, open **Device**, choose **Enable**, and confirm **Enable and Restart**. Let Windows restart before using the stock controller environment.

To remove the app, use its normal entry in **Windows Settings → Apps → Installed apps** and choose **Uninstall**. The uninstaller prepares a safe controller handoff; if it reports that preparation did not complete, do not force-remove controller components—resolve the reported issue and retry. Your Addon settings, game profiles, shortcuts, and logs are retained after uninstall so they are available if you reinstall. The included ClawHUD is removed with the app.

## License

Steam Addon for Claw is distributed under the license in [`LICENSE`](LICENSE). Third-party components remain subject to their own licenses and notices included with the installation.
