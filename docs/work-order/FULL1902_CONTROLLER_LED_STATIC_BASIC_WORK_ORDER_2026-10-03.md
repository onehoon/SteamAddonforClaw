# Full1902 Controller LED — Basic Static Control Work Order

Status: Implementation work order  
Date: 2026-10-03  
Scope: One focused production PR  
Target: SteamAddonforClaw standalone Full1902 architecture

## 1. Goal

Add the smallest useful MSI Claw controller LED feature to the main Controller page.

The product scope is intentionally limited to:

- LED On / Off;
- global brightness, 0..100;
- one global Static RGB color applied uniformly to every controller LED;
- persisted desired state;
- reapply after the real Full1902 controller lifecycle reacquires PID1902.

The default must be:

~~~text
Enabled = false
Brightness = 100
Color = #FFFFFF
~~~

In other words, a new install starts with the controller LEDs OFF. Brightness and color are remembered independently so turning the LED back on restores the saved values.

This PR must NOT implement the broader CTW lighting feature set.

## 2. Explicitly out of scope

Do not add:

- Battery / SoC color mode;
- Breathing;
- Wave;
- Color Cycle;
- Rainbow;
- speed controls;
- direction controls;
- left/right/buttons per-zone controls;
- individual physical LED controls;
- animated-frame compositor abstractions;
- a separate LED detail page;
- Overlay / Quick Settings LED controls;
- Game Profile LED overrides;
- Center M parity work;
- new reverse engineering;
- a new HID library;
- HidSharp;
- a new device-discovery manager;
- a new controller authority;
- a new suspend/resume watcher;
- a generic lighting framework;
- a generic device-feature state machine;
- SyncToROM / EEPROM persistence on UI edits.

If a later PR needs richer effects, extend from this simple static contract then. Do not pre-build that future system now.

## 3. Required source references

Implement against current repository code and the following evidence.

### 3.1 Full1902 authority

Read together before changing code:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

Important invariant:

~~~text
Center M Enabled
-> MSI / stock controller authority
-> Addon must not write controller LED state

Center M Disabled
-> Addon Runtime controller authority
-> desired physical PID = 1902
-> LED is an Addon-owned controller hardware setting
~~~

Steam/BPM presentation switching between Xbox360 and SteamDeck must never change LED state.

### 3.2 Current Addon code to reuse

Inspect and reuse the current implementations rather than adding parallel infrastructure:

- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawHardware.cs
- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs
- src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawModeWriter.cs
- src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawRawHidTransport.cs
- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs
- src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
- src/SteamInputAddonforClaw/Settings/AppSettings.cs
- src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs
- src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
- src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
- src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
- src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
- src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
- src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs

The existing MsiClawControlHidResolver already owns exact physical control-HID resolution.

The existing WindowsMsiClawRawHidTransport already owns the native 64-byte MSI HID write/read transport.

Do not duplicate either responsibility.

### 3.3 Existing Addon LED research

Use:

- docs/RE_MSI_Joystick_LED.md

The established static packet surface is:

~~~text
PID1901 control HID: UsagePage 0xFFA0 / Usage 0x0001
PID1902 control HID: UsagePage 0xFFF0 / Usage 0x0040
Output report: 64 bytes

Preamble:
0F 00 00 3C

WriteProfile:
21 01
~~~

The static profile block fields are:

~~~text
[0]      0x0F
[3]      0x3C
[4]      0x21
[5]      0x01
[6..7]   firmware-specific RGB base address
[8]      0x20
[9]      0x00
[10]     0x01       Static mode
[11]     0x09       required constant
[12]     0x03       static speed value
[13]     brightness 0..100
[14..40] 9 * RGB triplets
~~~

Physical zones remain:

~~~text
0..3 = right joystick ring
4..7 = left joystick ring
8    = controller buttons
~~~

This PR always writes the same RGB value to all nine zones.

### 3.4 CTW production reference

Use the deployed CTW implementation from:

Repository:
onehoon/ClawTweaks-Dev

Branch:
release/v0.3.98.0

Primary file:
XboxGamingBarHelper/Devices/MSIClaw/MsiClawLedController.cs

Relevant production methods:

- TrySetLedColor
- TrySetSolidEffect
- TrySetZonesEffect
- BuildBlock
- BuildRawBlock
- ResolveRgbAddr

Do not port CTW's HidSharp/device-discovery/helper architecture.

Port only the established static protocol behavior into the existing SteamAddon HID architecture.

The production CTW Static write does more than one base block write. It fills the four firmware frame slots with the same static frame:

~~~text
base RGB address
-> header block containing Static mode + frame 0

base + 32
-> raw 27-byte identical RGB frame

base + 32 + 27
-> raw 27-byte identical RGB frame

base + 32 + 54
-> raw 27-byte identical RGB frame
~~~

Keep this exact four-write static behavior. It prevents stale animated content left by a previous tool or firmware configuration from later appearing.

Do not write a full effect header into the follow-on raw frame addresses.

CTW's production Static path does not issue SyncToROM. SteamAddon must likewise avoid SyncToROM for normal LED settings.

### 3.5 Current HHC firmware evidence

Current Handheld Companion source:

Valkirie/HandheldCompanion  
HandheldCompanion/Devices/MSI/ClawA1M.cs

currently contains the following RGB address table:

~~~text
0x0163 -> 0x01FA
0x0166 -> 0x024A
0x0167 -> 0x024A

0x0211 -> 0x01FA
0x0217 -> 0x024A
0x0219 -> 0x024A

0x0308 -> 0x024A

MS-1T91 / EX:
0x0411 -> 0x024A
0x0414 -> 0x024A
~~~

This supersedes the stale statement in docs/RE_MSI_Joystick_LED.md that EX 0x0411 is absent from the current table.

Update that research document as part of this PR.

## 4. Product contract

The complete user-visible contract for this PR is:

~~~text
Joystick LED
  Enabled      On / Off
  Brightness   0..100
  Color        one RGB color
~~~

Nothing else.

### 4.1 Default

The persisted default must be OFF.

Suggested shape:

~~~csharp
public sealed record ControllerLedSettings(
    bool Enabled,
    int Brightness,
    byte Red,
    byte Green,
    byte Blue)
{
    public static ControllerLedSettings Default { get; }
        = new(false, 100, 255, 255, 255);
}
~~~

Exact naming may follow repository conventions, but keep the contract this small.

Brightness must be validated to 0..100.

Do not model Off by destroying the remembered brightness/color.

Effective hardware brightness is:

~~~text
Enabled == true  -> saved Brightness
Enabled == false -> 0
~~~

Therefore:

~~~text
Off
-> persisted Enabled=false
-> saved Brightness and RGB remain untouched
-> hardware packet uses brightness 0

On
-> persisted Enabled=true
-> hardware packet uses saved Brightness and RGB
~~~

## 5. Persistence ownership

Add ControllerLedSettings to the existing AppSettings record as an init-only property with ControllerLedSettings.Default.

Use the same settings.json file and the same StartupSettingsCoordinator authority.

Do not create:

- a second LED settings file;
- Registry persistence;
- firmware/EEPROM persistence;
- a CTW-compatible settings file;
- an LED cache.

Preferred pattern:

~~~csharp
public ControllerLedSettings ControllerLed { get; init; }
    = ControllerLedSettings.Default;
~~~

StartupSettingsCoordinator should expose the current settings and one narrow validated mutation method.

Follow the existing save-then-current pattern.

A settings.json created before this feature must deserialize with the default OFF state when the property is absent.

## 6. Static HID implementation

### 6.1 Keep the implementation small

Preferred production pieces:

~~~text
ControllerLedSettings
MsiClawLedProtocol
MsiClawLedController
~~~

MsiClawLedProtocol should be pure packet/address logic.

MsiClawLedController should resolve the current exact owned control HID and execute the static write sequence.

Do not add LedCompositor, EffectEngine, LightingManager, LedSession, LedAuthority, or similar abstractions.

### 6.2 Exact control HID only

Under Addon authority, LED writes target PID1902 control HID:

~~~text
VID 0x0DB0
PID 0x1902
UsagePage 0xFFF0
Usage 0x0040
~~~

Use the existing MsiClawControlHidResolver and the same strong physical identity checks already used by native-mode control.

Do not target:

- the DirectInput gamepad collection;
- the rumble endpoint;
- an arbitrary first VID/PID match;
- a weak or ambiguous identity.

If exact identity/control HID cannot be proven, fail closed and perform no LED write.

### 6.3 Firmware version / RGB address

CTW obtains USB bcdDevice via HidDevice.ReleaseNumberBcd.

SteamAddon must not add HidSharp only for this.

Extend the existing native HID seam narrowly to read the same USB version number from the exact already-resolved HID path, preferably with HidD_GetAttributes and HIDD_ATTRIBUTES.VersionNumber.

Conceptual native shape:

~~~csharp
[StructLayout(LayoutKind.Sequential)]
private struct HIDD_ATTRIBUTES
{
    public int Size;
    public ushort VendorID;
    public ushort ProductID;
    public ushort VersionNumber;
}
~~~

Initialize Size correctly before HidD_GetAttributes.

Keep this capability in the current Windows native HID transport/API layer rather than inventing a second Windows HID stack.

### 6.4 Address policy

Support the known firmware table listed in section 3.5.

For this SteamAddon implementation, use exact known firmware matches for mutation.

Unknown firmware:

~~~text
unknown bcdDevice
-> log unsupported firmware
-> perform zero profile writes
-> return unavailable/apply-failed
~~~

Do not probe arbitrary EEPROM addresses.

Do not silently use 0x01FA or 0x024A for an unknown firmware.

This is intentionally fail-closed while the currently known CTW/HHC firmware table covers the supported devices.

### 6.5 Packet generation

For a requested RGB value:

~~~text
frame = [R,G,B] repeated exactly 9 times
~~~

Build a 64-byte header packet at the resolved base address with:

~~~text
mode       = 0x01
constant   = 0x09
speed      = 0x03
brightness = Enabled ? clamp(Brightness, 0, 100) : 0
frame      = uniform 9-zone RGB
~~~

Then build three raw 64-byte packets with the same 27-byte frame at:

~~~text
base + 32
base + 59
base + 86
~~~

Each raw packet uses:

~~~text
[0]    0x0F
[3]    0x3C
[4]    0x21
[5]    0x01
[6..7] target address
[8]    27
[9..35] frame payload
~~~

Execute the four writes in order.

Stop on the first failed write and report the apply failure.

Do not issue command 0x22 / SyncToROM.

No readback is required on every ordinary UI edit. CTW's deployed static setter is the production behavior reference for this PR.

## 7. Runtime ownership and lifecycle

LED is a controller hardware setting. It is not a routing setting and not a virtual-presentation setting.

### 7.1 One desired-state authority

~~~text
AppSettings.ControllerLed
= single persisted desired LED state
~~~

Do not maintain another durable LED state in AddonProcessHost, ControllerPage, or the HID controller.

### 7.2 Initial Full1902 ownership

After Center M Disabled startup successfully establishes the normal owned PID1902 physical session, apply the persisted LED setting once.

This is important even for the default:

~~~text
new install
ControllerLed.Enabled = false
successful PID1902 ownership
-> apply Static packet with brightness 0
-> controller LEDs are actually off
~~~

Do not merely show an Off toggle while leaving firmware-restored LEDs physically on.

LED apply failure must not fail the entire Full1902 controller startup if controller ownership itself is healthy. Log the failure and keep the controller usable.

### 7.3 User mutation

The frontend mutation sequence should be:

~~~text
validate requested LED settings
-> persist desired settings
-> if Addon currently owns a healthy PID1902 physical session:
       best-effort apply immediately
   else:
       no unsafe write
-> return authoritative persisted settings
~~~

A transient HID apply failure must NOT roll the saved desired state back.

Reason:

~~~text
persisted desired state remains authoritative
-> next successful ownership/recovery can reapply it
~~~

This matches normal hardware-setting behavior and avoids making a temporary PnP/HID failure destroy a valid user preference.

### 7.4 Resume / PnP / physical recovery

Do not add a new power watcher or PnP watcher.

Reuse the existing real Full1902 recovery flow.

AddonProcessHost already has the shared owned-controller recovery seam:

~~~text
RequestOwnedControllerRecovery(...)
-> RecoverOwnedControllerPhysicalInputAsync(...)
-> physical.RecoverLostInputAsync(...)
~~~

After a recovery actually succeeds and ownership is re-established, reapply the current persisted LED setting once.

This naturally covers the supported real lifecycle paths that already flow through physical recovery, including device loss/PnP return and resume-related reacquisition.

Do not reapply on unrelated Device Arrival when LiveInputSource is already healthy.

Do not add an LED epoch, barrier, session state machine, or independent reconcile loop.

### 7.5 Presentation changes

These events must perform zero LED writes:

~~~text
Xbox360 -> SteamDeck
SteamDeck -> Xbox360
Steam game start/end
Big Picture enter/exit
Overlay show/hide
~~~

LED state belongs to the physical controller, not to VIIPER presentation.

### 7.6 Center M Enabled

When stock authority is active:

~~~text
Addon LED mutation -> unavailable / no hardware write
Addon startup -> no LED apply
Addon recovery -> no LED apply
~~~

Do not compete with Center M as a second lighting writer.

The frontend availability is a derived presentation fact only. It must not become another authority source.

If adding a bootstrap availability flag is the lowest-churn UI solution, name it narrowly (for example ControllerLedAvailable), derive it from existing supported-hardware + Addon-authority/runtime facts, and never persist it.

## 8. Frontend contract

Expose the small typed ControllerLedSettings contract through FrontendSettingsSnapshot.

Add one typed mutation to IAddonFrontendControl, conceptually:

~~~csharp
Task<FrontendSettingsSnapshot> SetControllerLedSettingsAsync(
    ControllerLedSettings settings,
    CancellationToken cancellationToken = default);
~~~

The in-process implementation must use StartupSettingsCoordinator for persistence and one host-provided narrow apply callback for hardware application.

Do not let InProcessAddonFrontendControl enumerate HID devices itself.

Because the frontend settings schema and RPC surface change, update the named-pipe protocol version from 45 to 46.

Add the version comment explaining the Controller LED settings member and SetControllerLedSettings RPC.

Pre-release policy remains:

~~~text
no old protocol compatibility shim
old peer -> handshake mismatch
~~~

Add the corresponding request record, client dispatch, server dispatch, serialization, and tests.

## 9. Main Controller UI

Do not create a detail page.

Add a small Controller Settings section after Button Mapping in ControllerPage.

Preferred layout:

~~~text
Controller

Button Mapping
  ...

Controller Settings

[ Joystick LED                         Off/On ]
  Brightness       [---------|----] 70
  Color            [ color swatch ]
~~~

Use one SettingsExpander titled Joystick LED.

Inside it:

1. Enabled SettingsCard with ToggleSwitch.
2. Brightness SettingsCard with Slider, Minimum=0, Maximum=100.
3. Color SettingsCard with a compact color-swatch/button that opens the standard WinUI ColorPicker in a Flyout, or the simplest equivalent that does not make the entire Controller page tall.

Alpha is not part of the product contract and must be disabled/ignored.

When Enabled is false:

- brightness and color controls remain visible;
- they are disabled for editing;
- their saved values are still shown;
- no value is reset.

Do not auto-expand into another page.

Do not add mode/effect controls.

### 9.1 UI initialization

Follow the existing ControllerPage pattern:

- load from FrontendBootstrapSnapshot.Settings;
- use the existing _isLoading guard so initialization never looks like a user edit;
- keep the page as a presentation/editor surface;
- do not write settings.json or HID directly from ControllerPage.

Add one narrow event such as:

~~~csharp
internal event EventHandler<ControllerLedSettings>? ControllerLedEditRequested;
~~~

### 9.2 Brightness write rate

Do not send one HID transaction for every raw Slider.ValueChanged while a pointer is dragged.

Use one small UI mutation debounce, approximately 200 ms, shared by LED edits, or an equivalent commit-on-user-settle approach.

The goal is only:

~~~text
continuous slider drag
-> coalesce intermediate values
-> persist/apply latest value
~~~

Do not build a general debounce framework.

Keep ordered mutation ownership in MainWindow, consistent with the existing Controller mapping save ownership.

Rapid Toggle/Color/Brightness edits must converge to the newest requested whole ControllerLedSettings record.

This is a normal user-interaction ordering requirement, not a reason to introduce epochs/managers.

## 10. Failure behavior

Hardware failure must be bounded and non-destructive.

Examples:

~~~text
control HID missing
ambiguous identity
unsupported firmware
native HID open failure
short/failed write
PnP disappearance during apply
~~~

Required response:

~~~text
do not change controller PID
do not alter HidHide
do not detach VIIPER
do not release Full1902 authority
do not roll settings back
log the LED apply failure
allow later startup/recovery/user mutation to retry
~~~

No LED failure is allowed to take the gamepad away from the user.

## 11. Tests

Add focused tests for the real contract.

### 11.1 Settings / persistence

Verify:

- ControllerLedSettings.Default.Enabled == false;
- default brightness == 100;
- default color == white;
- missing ControllerLed property in older settings JSON loads Default;
- brightness below 0 or above 100 is rejected or normalized according to one explicit validation rule;
- Off preserves stored brightness and RGB.

### 11.2 Protocol unit tests

Verify exact bytes for a known firmware/static color.

At minimum:

- 64-byte report;
- preamble;
- WriteProfile 0x21 0x01;
- address;
- write length;
- Static mode 0x01;
- constant 0x09;
- speed 0x03;
- requested/effective brightness;
- all nine RGB triplets identical;
- Off uses hardware brightness 0.

Verify the exact follow-on addresses:

~~~text
base
base + 32
base + 59
base + 86
~~~

Verify the three follow-on writes contain raw 27-byte payloads, not full effect headers.

Verify no SyncToROM packet is generated.

### 11.3 Firmware address tests

Cover all known table entries, especially:

~~~text
0x0411 -> 0x024A
0x0414 -> 0x024A
~~~

Also prove:

~~~text
unknown firmware
-> zero profile writes
~~~

### 11.4 Exact-device tests

Prove LED mutation:

- uses the PID1902 0xFFF0/0x0040 control HID under Addon ownership;
- requires strong physical identity;
- rejects ambiguous candidates;
- does not use the DirectInput gamepad HID;
- does not use the rumble endpoint.

### 11.5 Lifecycle tests

Prove:

~~~text
successful initial Full1902 ownership
-> saved LED applied once

default saved state
-> brightness 0 applied once

successful real physical recovery
-> saved LED reapplied once

failed recovery / no ownership
-> zero LED writes

presentation-only switch
-> zero LED writes

Center M / stock authority
-> zero LED writes
~~~

Do not add synthetic tests for pathological instruction-level races.

### 11.6 Frontend transport tests

Update the existing contract/named-pipe tests for:

- protocol v46;
- settings snapshot round trip;
- SetControllerLedSettings request/response;
- invalid request failure behavior;
- default state serialization.

### 11.7 UI tests

Verify:

- Controller page contains one Joystick LED SettingsExpander;
- no separate LED navigation/detail page exists;
- Enabled, Brightness, Color are the only LED user controls;
- UI initialization does not emit a mutation;
- disabled state disables brightness/color editing without erasing values;
- a burst of brightness edits converges to the latest value.

## 12. Documentation update

Update docs/RE_MSI_Joystick_LED.md in the same PR.

Required corrections:

1. Replace the stale claim that EX 0x0411 is absent from the current firmware table.
2. Record current HHC evidence:
   - 0x0411 -> 0x024A
   - 0x0414 -> 0x024A
3. Record that CTW release/v0.3.98.0 ships a production static path using:
   - Static mode 0x01;
   - speed 0x03;
   - 9 uniform RGB zones for solid color;
   - base header + three identical contiguous raw frame writes;
   - no SyncToROM in the normal setter.
4. Clearly state that SteamAddon currently implements only the Basic Static subset from this work order.
5. Keep animated-effect research as historical/reference material, but mark it out of scope for the production feature.

Do not rewrite the document to claim untested behavior beyond the cited CTW/HHC evidence and the actual SteamAddon hardware acceptance result.

## 13. Manual hardware acceptance

On the target MSI Claw EX with Center M Disabled / Addon authority:

1. Start from a clean/default settings file.
   Expected: the Addon obtains PID1902 and LEDs become Off.

2. Toggle Joystick LED On.
   Expected: all stick-ring LEDs and the button LED use the same saved/static color.

3. Test brightness at representative values:
   - 0;
   - low value;
   - 50;
   - 100.

4. Change color.
   Expected: all nine physical LED zones change uniformly.

5. Set a non-default color and brightness, turn Off, then turn On.
   Expected: saved color and brightness return.

6. Restart the Runtime/app in the normal supported Full1902 lifecycle.
   Expected: persisted LED setting is reapplied after physical ownership is established.

7. Sleep/Resume.
   Expected: controller recovery remains healthy and persisted LED state is restored/reapplied when the physical control HID is available.

8. If practical, exercise one real PID1902 physical disappearance/PnP re-enumeration.
   Expected: normal controller recovery succeeds and LED desired state is reapplied afterward.

9. Confirm Xbox360 <-> SteamDeck presentation switching causes no LED change.

10. Confirm normal input, M1/M2, HidHide isolation, VIIPER presentation, and rumble remain unaffected.

If the known EX firmware is not an exact supported table entry, stop the LED mutation test. Do not probe arbitrary addresses.

## 14. Expected implementation footprint

Exact filenames may change to fit the repository, but keep the change approximately within these areas:

~~~text
src/SteamInputAddonforClaw.Contracts/
  ControllerLed/... small settings contract
  Frontend/FrontendContracts.cs

src/SteamInputAddonforClaw/
  Settings/AppSettings.cs
  Settings/StartupSettingsCoordinator.cs
  Devices/MSI/Claw/MsiClawLedProtocol.cs
  Devices/MSI/Claw/MsiClawLedController.cs
  Devices/MSI/Claw/WindowsMsiClawRawHidTransport.cs
  Frontend/InProcessAddonFrontendControl.cs
  Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.FrontendTransport/
  FrontendWire.cs
  matching named-pipe client/server dispatch files

src/SteamInputAddonforClaw.UI/
  Views/ControllerPage.xaml
  Views/ControllerPage.xaml.cs
  MainWindow.xaml.cs

tests/
  focused settings/protocol/runtime/frontend/UI tests

docs/
  RE_MSI_Joystick_LED.md
~~~

Do not use this feature as a reason to refactor unrelated controller ownership, transport, UI navigation, settings, or frontend infrastructure.

## 15. Definition of done

The PR is complete only when all of the following are true:

~~~text
[ ] Default LED setting is OFF.
[ ] Only On/Off + Brightness + one Static color are exposed.
[ ] No battery/effect/per-zone feature is added.
[ ] No separate LED detail page is added.
[ ] Existing exact Full1902 control-HID resolution is reused.
[ ] Existing native HID transport is reused.
[ ] No HidSharp dependency is added.
[ ] Known firmware address table includes current EX 0x0411/0x0414 evidence.
[ ] Unknown firmware fails closed without profile writes.
[ ] Static write uses CTW production four-slot fill behavior.
[ ] No SyncToROM is issued.
[ ] Saved Off is physically applied after initial Addon ownership.
[ ] Saved state is reapplied after real physical recovery/resume/PnP recovery.
[ ] Presentation switches do not touch LEDs.
[ ] Stock/Center M authority never receives Addon LED writes.
[ ] UI slider does not flood the HID endpoint.
[ ] Frontend protocol version is bumped and transport tests pass.
[ ] Existing Full1902 controller lifecycle tests still pass.
[ ] docs/RE_MSI_Joystick_LED.md is corrected/updated.
[ ] Manual EX smoke test passes.
~~~

## 16. Overengineering guard

The target architecture is intentionally:

~~~text
one persisted ControllerLedSettings
        |
one static packet builder/controller
        |
existing exact control-HID resolver
        |
existing raw HID transport
~~~

Do not solve hypothetical future RGB requirements in this PR.

Do not add synchronization/state for theoretical instruction-level races.

Only preserve the real supported lifecycle:

- startup/restart;
- Sleep/Hibernate/Resume;
- physical device loss/PnP return;
- PID1902 ownership recovery;
- Center M authority boundary;
- actual HID operation failure.

The objective is not to create a lighting subsystem. It is to provide one reliable Static LED control with OFF as the default.
