# Work Order — Full1902 Controller Vibration Strength (Firmware-Backed Left / Right)

> Date: 2026-10-03  
> Repository baseline reviewed: main at 08afb42b3d4b7ac4d174d2f88cfdc0ac22a709e9  
> Scope: one production PR  
> Product model: standalone Full1902 application; CTW/HHC are reference implementations only

## 1. Goal

Add a production Controller-page Vibration Strength control for supported MSI Claw hardware.

The feature must expose the MSI firmware motor-ceiling values directly:

- Left Motor: firmware LM at profile address 0x0022
- Right Motor: firmware RM at profile address 0x0023
- valid user range: 0..100 percent

The firmware values themselves are the source of truth.

Do not create an independent software rumble-strength preference and do not duplicate these values into settings.json.

The user-observed MSI Center M factory/default presentation on the current test device is 50% / 50%. That observation is useful validation evidence only. It is NOT a constant, fallback, migration value, or default owned by this Addon.

The UI must always derive the displayed values from actual firmware readback.

## 2. Reviewed authority and reference material

Implementation must remain consistent with the current Full1902 authority documents:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

Feature-specific repository references reviewed:

- docs/RE_MSI_ControllerVibration.md
- docs/RE_MSI_Joystick_LED.md
- docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
- docs/work-order/FULL1902_PRODUCTION_RUMBLE_FEEDBACK_WORK_ORDER.md
- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs
- src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawModeWriter.cs
- src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawRawHidTransport.cs
- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawRumbleSink.cs
- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
- src/SteamInputAddonforClaw/Feedback/PresentationRumbleFeedback.cs
- src/SteamInputAddonforClaw/Feedback/TwoMotorRumble.cs
- src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
- src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
- src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs
- src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
- src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
- src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
- src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

External reference implementations reviewed:

- onehoon/ClawTweaks-Dev at 3977f6b1f479fd877df24a6ffbc016438f281bdc
  - XboxGamingBarHelper/Labs/ClawButtonMonitor.cs
- Valkirie/HandheldCompanion at 4674a4c8c378490f92207789c6da7493b79bc825
  - HandheldCompanion/Controllers/IController.cs
  - HandheldCompanion/Controllers/MSI/DClawController.cs

HHC demonstrates the generic virtual-controller pattern of applying a software multiplier before its physical HID report.

CTW demonstrates why MSI needs the software multiplier and the firmware ceiling treated as separate concepts when both virtual-relay and stock-hardware modes are supported. Its current code explicitly documents that the firmware ceiling still caps the motor after software scaling.

This Addon must NOT copy that dual-authority model. The requested feature is MSI Center M-style firmware strength control, while the existing Full1902 production rumble stream remains unchanged.

## 3. Product semantics

The setting means:

> The persistent MSI firmware ceiling for each physical rumble motor.

It does NOT mean:

- a game-profile rumble scale;
- a Full1902 software gain;
- an Xbox360-only gain;
- a SteamDeck-only gain;
- a second runtime rumble authority;
- a setting stored by the Addon.

Therefore the production path remains:

~~~text
Game
-> current virtual presentation (Xbox360 or SteamDeck)
-> existing normalized TwoMotorRumble
-> existing MsiClawRumbleSink
-> physical MSI rumble report
-> firmware LM/RM ceiling
-> motor
~~~

Do not multiply TwoMotorRumble by the UI percent.

Do not modify Xbox360RumbleFeedbackBridge or SteamDeckRumbleFeedbackAdapter for this feature.

Do not alter the existing deadman, STOP retry, suspend, presentation-switch, teardown, generation, endpoint-recovery, or physical-rumble failure policy.

## 4. UI placement

Modify the existing Controller page directly.

Do NOT add a separate Controller Settings group/header.

Add one SettingsExpander after the existing controller mapping controls:

~~~text
[Vibration Strength]  <icon>  >
    Left Motor
    [ 0 ---------------- 100 ]  50%   [Test]

    Right Motor
    [ 0 ---------------- 100 ]  50%   [Test]
~~~

Requirements:

- Header: Vibration Strength
- Include a built-in WinUI / Fluent icon in SettingsExpander.HeaderIcon.
- Use an existing built-in FontIcon/Symbol style consistent with the current application. Do not add a custom image asset solely for this card.
- SettingsExpander is always collapsed by default.
- Do not persist expansion state.
- Left and Right are separate SettingsCard rows.
- Each row has:
  - one native Slider;
  - visible integer percent text;
  - one Test button.
- Slider:
  - Minimum=0
  - Maximum=100
  - integer values
  - StepFrequency=1
- No single combined Left/Right slider.
- No Coming Soon cards for unrelated controller features.

## 5. Firmware is the only persistence authority

Do NOT add vibration strength to:

- AppSettings
- SettingsStore
- StartupSettingsCoordinator
- ProfileStore
- game profiles
- any new JSON file
- registry
- an ownership marker

The firmware already persists LM/RM.

Expected capture behavior:

~~~text
open/refresh Controller page
-> resolve the current supported MSI Claw command HID
-> read 0x0022
-> read 0x0023
-> validate both
-> render actual readback
~~~

If Center M, firmware, another tool, or a previous boot changed the values, the Addon must show the actual current values on the next authoritative capture.

There is no Addon default.

Never silently substitute:

~~~text
100 / 100
50 / 50
previous UI values
settings.json values
~~~

when the firmware read fails.

A failed/ambiguous read means the control is unavailable until a later successful refresh.

## 6. Firmware command protocol

Implement only the narrow motor-ceiling profile operations established by the existing RE.

Command HID endpoints:

~~~text
PID1901: usage page 0xFFA0 / usage 0x0001
PID1902: usage page 0xFFF0 / usage 0x0040
~~~

Addresses:

~~~text
Left  = 0x0022
Right = 0x0023
~~~

WriteProfile shape:

~~~text
0F 00 00 3C 21 01 <addrHi> <addrLo> 01 <value> ...
~~~

SyncToROM:

~~~text
0F 00 00 3C 22 ...
~~~

ReadProfile follows the already documented working profile-read protocol:

~~~text
request:
0F 00 00 3C 04 01 <addrHi> <addrLo> 01 ...

expected response:
10 00 00 3C 05 01 <addrHi> <addrLo> 01 <value> ...
~~~

The parser must validate at minimum:

- inbound report id 0x10;
- command marker 0x3C;
- ReadProfile ack 0x05;
- requested address;
- requested length 0x01;
- value is within 0..100.

Reject malformed, stale, wrong-address, wrong-length, or out-of-range responses.

Do not infer a value from an unrelated response frame.

## 7. Reuse the existing command-HID identity path

Do not create a loose VID/PID-only writer.

Reuse the existing Full1902 identity and command-HID machinery:

- WindowsControllerDeviceEnumerator
- MsiClawPhysicalIdentity
- MsiClawControlHidResolver / ResolveCommand
- MsiClawControlHidDevice
- IMsiClawRawHidTransport / WindowsMsiClawRawHidTransport

Follow the same strong-identity rules used by native mode operations.

Every capture/mutation operation must freshly resolve the current command interface. Do not retain a device path across PnP re-enumeration, sleep/resume, PID1901/PID1902 changes, or controller reconnects.

A supported operation requires one unambiguous strongly identified MSI Claw physical device and one exact command HID.

Ambiguity fails closed.

## 8. Add one narrow firmware client, not a manager hierarchy

A small feature-specific hardware/client type is appropriate, for example:

~~~text
MsiClawVibrationStrengthClient
MsiClawVibrationProfileCommand
~~~

Exact names may follow nearby conventions.

Responsibilities only:

- capture Left/Right firmware values;
- validate requested percentages;
- serialize one profile transaction at a time;
- write only changed channel bytes;
- SyncToROM;
- read back and report the actual result.

Do not add:

- VibrationManager;
- RumbleManager;
- ControllerSettingsManager;
- firmware state machine;
- epoch/generation framework;
- retry scheduler;
- background reconciler;
- polling timer;
- duplicate device authority.

One small SemaphoreSlim or equivalent inside the firmware client is acceptable because one EEPROM read/write/sync/readback transaction must not interleave with another normal UI mutation.

Do not add more synchronization than that transaction requires.

## 9. Safe mutation sequence

The frontend mutation should accept the complete desired pair:

~~~text
LeftPercent
RightPercent
~~~

Range validation:

~~~text
0 <= LeftPercent <= 100
0 <= RightPercent <= 100
~~~

Before any mutation:

1. confirm supported hardware;
2. confirm the current Center M authority state is exactly Disabled;
3. freshly resolve the strongly identified current MSI command HID;
4. read both current values.

Read-only capture may report actual firmware values while stock authority is active, but firmware mutation must not compete with MSI Center M / stock controller authority.

For each channel that changed, use the conservative verified sequence:

~~~text
read current pair

if Left changed:
    WriteProfile 0x0022 = requested left
    SyncToROM
    read 0x0022
    require exact readback

if Right changed:
    WriteProfile 0x0023 = requested right
    SyncToROM
    read 0x0023
    require exact readback

final read of both
return actual firmware pair
~~~

If a channel is unchanged, do not consume an EEPROM write cycle for it.

Do not rewrite the complete controller profile.

Do not write an unverified firmware address.

Do not add arbitrary periodic reassertion.

## 10. Partial failure policy

Left and Right are independent firmware values.

If Left commits successfully and Right then fails, do NOT attempt a speculative rollback of Left.

Instead:

~~~text
mutation result = Failed
best-effort final authoritative read
UI = whatever the firmware actually reports
failure message = mutation/readback did not fully verify
~~~

This avoids extra EEPROM writes and avoids fabricating atomicity the hardware protocol does not provide.

A later user action can retry the desired values.

If final readback itself is unavailable, return the feature as unavailable/failed and do not display the requested values as though they landed.

## 11. 500 ms UI debounce

Persistent profile writes must not occur on every Slider.ValueChanged tick.

Use a 500 ms settle debounce.

Required behavior:

~~~text
Slider moves
-> update percent label immediately
-> restart one 500 ms debounce
-> no firmware write yet

500 ms after the latest change
-> disable the vibration sliders/Test buttons for the bounded mutation
-> submit the complete current Left/Right pair once
-> Runtime performs read/write/sync/readback
-> render authoritative returned firmware values
-> re-enable controls
~~~

This is not a live-preview slider.

Do not send a preview rumble while dragging.

Do not use every pointer movement as a firmware write.

If both sliders are moved within one debounce window, coalesce them into one pair mutation.

The Controller UI may own this feature-local debounce because there is no second settings surface or persisted Addon setting to coordinate.

Do not introduce a global save manager for it.

## 12. Test buttons

Add one Test button to each motor row.

Semantics:

~~~text
Left Test:
    LargeMotor = 0xFFFF
    SmallMotor = 0
    hold about 1 second
    STOP

Right Test:
    LargeMotor = 0
    SmallMotor = 0xFFFF
    hold about 1 second
    STOP
~~~

The test deliberately sends full-scale runtime motor magnitude.

The firmware LM/RM ceiling is what produces the user-selected physical strength.

Do NOT additionally scale the test pulse in software.

Do NOT write firmware from the Test action.

Do NOT use generic XInput slot enumeration/broadcasting for this production feature. That can affect unrelated controllers and is unnecessary in Full1902 Addon authority mode.

## 13. Test must reuse the existing Full1902 physical rumble sink

The production Full1902 presentation already owns the one MsiClawRumbleSink used by Xbox360 and SteamDeck feedback.

Reuse that same sink.

Preferred narrow implementation:

- add a bounded production motor-test method to the existing MsiClawAddonPresentation / IMsiClawAddonPresentation seam, or an equivalently small seam that invokes its already-owned IPhysicalRumbleSink;
- do not instantiate a second MsiClawRumbleSink;
- do not create a second physical endpoint resolver/writer for the test.

The test method must:

1. reject when the presentation/physical rumble sink is unavailable;
2. reject when the Full1902 controller is not in a usable live owned state;
3. write the selected one-channel full-scale pulse;
4. await approximately one second;
5. execute TwoMotorRumble.Stopped in finally;
6. report failure if the final STOP cannot be confirmed.

The existing MsiClawRumbleSink STOP retry remains the only physical STOP retry policy.

Do not add another retry loop.

## 14. Test availability and interaction

Firmware values and test availability are separate facts.

The UI may successfully read the firmware while the live Full1902 physical-rumble path is unavailable.

Therefore the frontend snapshot should expose a distinct TestAvailable fact.

Examples:

~~~text
supported MSI Claw + firmware read succeeds + Center M Enabled
-> values may be shown
-> mutation disabled
-> Test disabled

Center M Disabled + firmware read succeeds + live Full1902 rumble sink available
-> values editable
-> Test enabled

PID1902 temporarily absent / PnP re-enumerating
-> capture/mutation fails closed
-> no guessed values
-> later refresh resolves fresh topology
~~~

Do not implement a fallback XInput broadcast test in Center M Enabled mode.

During the 500 ms debounce or an in-flight firmware mutation, disable both Test buttons. This ensures Test always represents a readback-verified firmware value without special flush-pending-slider machinery.

While one Test RPC is in flight, disable both Test buttons so Left/Right tests cannot overlap from the supported single Main UI session.

No generalized multi-client test arbitration is required.

## 15. Test versus live game rumble

Do not build a new rumble-arbitration authority solely for the settings Test button.

The test is a short explicit diagnostic using the same physical sink.

If a game is actively issuing feedback at the same moment, existing presentation callbacks may affect what is felt. That does not justify a second feedback state machine.

The safety requirement is only:

- bounded one-second pulse;
- selected channel only;
- finally STOP;
- existing physical STOP retry;
- teardown/suspend owner remains authoritative.

Do not disarm/rebuild the active presentation merely to make the Test button exclusive.

## 16. Frontend contract

Do not add these values to FrontendSettingsSnapshot because they are not App settings.

Add dedicated typed contracts, following current frontend style.

Recommended conceptual shape:

~~~csharp
public sealed record FrontendControllerVibrationStrengthSnapshot(
    bool Available,
    bool Writable,
    bool TestAvailable,
    int? LeftPercent,
    int? RightPercent,
    string Status);

public enum FrontendControllerVibrationStrengthMutationOutcome
{
    Succeeded,
    Unavailable,
    Failed
}

public sealed record FrontendControllerVibrationStrengthMutationResult(
    FrontendControllerVibrationStrengthMutationOutcome Outcome,
    FrontendControllerVibrationStrengthSnapshot Snapshot,
    string? FailureMessage);

public enum FrontendControllerVibrationMotor
{
    Left,
    Right
}

public enum FrontendControllerVibrationTestOutcome
{
    Succeeded,
    Unavailable,
    Failed
}

public sealed record FrontendControllerVibrationTestResult(
    FrontendControllerVibrationTestOutcome Outcome,
    string? FailureMessage);
~~~

Exact naming can follow current source conventions, but preserve the semantics.

Expose narrow frontend operations:

~~~text
CaptureControllerVibrationStrengthAsync(...)
SetControllerVibrationStrengthAsync(leftPercent, rightPercent, ...)
TestControllerVibrationMotorAsync(motor, ...)
~~~

Use safe default interface implementations returning Unavailable only if that matches the current IAddonFrontendControl compatibility pattern and avoids unrelated test-double churn.

## 17. Frontend transport

Extend the existing desktop named-pipe transport only.

Required areas:

- FrontendContracts.cs
- FrontendWire request/enum/codec definitions
- NamedPipeAddonFrontendClient
- NamedPipeAddonFrontendServer
- InProcessAddonFrontendControl

Bump FrontendTransportProtocol.CurrentVersion from 45 to 46.

Add a version-history comment explaining that v46 introduces the firmware-backed Left/Right Controller Vibration Strength capture/mutation/test RPCs.

Do not add another pipe or IPC service.

## 18. Runtime composition

Compose one narrow firmware client against the existing MSI device enumeration, physical identity, command-HID resolver, and raw HID transport.

InProcessAddonFrontendControl should receive only the narrow feature dependency/delegates it needs.

Mutation must re-check current Center M Disabled state at the real hardware mutation boundary.

Do not infer writability from a stale UI/bootstrap value.

The Test operation must route into the existing Full1902 presentation/physical-rumble owner and must not bypass its current endpoint/session validation.

## 19. Controller-page behavior

ControllerPage must perform an authoritative vibration capture when the page is initialized/activated and after a transport error that leaves the result uncertain.

Rendering rules:

- successful read: show exact firmware values;
- read unavailable: show em dash / unavailable state, not 50 or 100;
- Writable=false: sliders disabled;
- TestAvailable=false: Test buttons disabled;
- mutation in progress: both sliders and both Test buttons disabled;
- test in progress: both Test buttons disabled;
- returned readback always replaces the draft after mutation completion.

Use an InfoBar or the page's existing compact failure pattern for read/write/test failures.

Do not display raw HID packet diagnostics in the normal Controller page.

## 20. No App settings migration

Do not touch AppSettings or SettingsStore for vibration strength.

No migration is required from older releases because there is no previous Addon vibration preference.

The firmware value survives application restart independently.

This is intentional:

~~~text
firmware = one persistence authority
UI = projection of firmware
Runtime = bounded reader/writer
~~~

## 21. Lifecycle requirements

### Restart / app crash

No special recovery file is needed.

A successfully synced motor value is already firmware-persistent.

After restart, capture actual firmware again.

If the process dies between two independent channel writes, the next capture reports the actual pair. Do not fabricate transaction rollback metadata.

### Sleep / Hibernate / Resume

Do not cache a HID path across suspend/resume.

A later capture/mutation resolves the present command HID again.

Do not add a background resume write or reassertion loop for vibration strength.

The existing Full1902 presentation suspend/resume path remains unchanged.

### Physical device loss / PnP re-enumeration

A read/write that loses its exact device fails closed.

Do not follow a stale device path.

The next operation re-enumerates and re-proves strong identity.

### Center M authority transition

Read-only capture may remain harmless, but firmware mutation is permitted only when the current authority check is exactly Center M Disabled.

Do not extend this feature into a new controller-authority gate.

### Process shutdown

Do not start new Test work after normal frontend shutdown admission closes.

An already-running test must reach its finally STOP, while existing presentation teardown remains the final safety owner.

Do not add shutdown state beyond the existing frontend/process lifecycle facts.

## 22. Logging

Add low-frequency structured logs only for meaningful operations.

Suggested events:

~~~text
ControllerVibrationCaptureSucceeded
ControllerVibrationCaptureUnavailable
ControllerVibrationMutationStarted
ControllerVibrationMutationSucceeded
ControllerVibrationMutationFailed
ControllerVibrationTestStarted
ControllerVibrationTestCompleted
ControllerVibrationTestFailed
~~~

Useful fields:

- LeftPercent / RightPercent where successfully read;
- Motor for Test;
- current PID/usage when useful;
- failure reason;
- readback mismatch evidence.

Do not log every Slider.ValueChanged tick.

Do not dump full HID buffers at Info.

## 23. Required protocol/unit tests

Add focused tests for the profile command builder/parser.

At minimum:

- ReadProfile request for 0x0022 / length 1 is exact.
- ReadProfile request for 0x0023 / length 1 is exact.
- WriteProfile 0x0022 builds the expected 64-byte frame.
- WriteProfile 0x0023 builds the expected 64-byte frame.
- SyncToROM frame is exact.
- parser accepts a valid 0x10 / 0x05 matching-address response.
- parser rejects wrong address.
- parser rejects wrong length.
- parser rejects wrong report id/ack/marker.
- parser rejects value > 100.

## 24. Required firmware-client tests

Cover:

### Capture

~~~text
left=50, right=50
-> Available
-> exact 50 / 50
-> zero writes
~~~

Also verify asymmetric values such as 35 / 70.

### No-op mutation

~~~text
current = 50 / 50
requested = 50 / 50
-> success
-> zero EEPROM writes
-> actual readback = 50 / 50
~~~

### Left only

~~~text
current = 50 / 50
requested = 70 / 50
-> write only 0x0022
-> SyncToROM
-> verify 0x0022=70
-> final pair 70 / 50
~~~

### Right only

Equivalent for 0x0023.

### Both

Verify ordered independent channel transactions and final pair.

### Bounds

Reject -1 and 101 without HID mutation.

### Write failure

No success is reported and no guessed requested value is returned.

### Sync failure

No success is reported.

### Readback mismatch

Requested 70, firmware returns 50 -> Failed with actual readback.

### Partial pair failure

Left commits, Right fails -> Failed; final snapshot reports the real observed state, not an invented rollback.

### Device re-enumeration

A stale/old device path is not retained for the next operation; fresh resolution is required.

Do not add pathological scheduler-interleaving tests.

## 25. Required frontend transport tests

Update the current frontend contract/wire tests for protocol v46.

Prove:

- Capture request/response round-trips.
- Left/Right pair mutation round-trips.
- Left/Right Test motor enum round-trips.
- invalid payload/value fails closed.
- server dispatch invokes the intended in-process operation once.
- client receives actual returned firmware snapshot.
- old v45 peer fails handshake at the protocol version boundary.

## 26. Required UI tests

Add focused Controller-page layout/behavior coverage.

At minimum verify:

- Vibration Strength appears directly on Controller page with no Controller Settings wrapper.
- expander default is collapsed.
- header contains an icon.
- Left Motor row exists.
- Right Motor row exists.
- both rows contain Slider + percent display + Test.
- slider range is 0..100.
- integer step is 1.
- initialization/readback render does not trigger mutation.
- ValueChanged changes draft/label only.
- firmware mutation is not issued before the 500 ms debounce settles.
- repeated ValueChanged within 500 ms coalesces.
- Test is disabled while debounce/mutation is pending.
- Test is disabled while another Test is active.
- mutation completion renders authoritative returned readback.
- unavailable capture does not display a fabricated 50/100.

Keep the test seam small. Do not create a production ViewModel hierarchy solely for timer tests.

## 27. Required rumble-test tests

Using the existing physical-rumble sink seam, prove:

Left Test:

~~~text
write TwoMotorRumble(0xFFFF, 0)
delay about 1 second
write TwoMotorRumble.Stopped
~~~

Right Test:

~~~text
write TwoMotorRumble(0, 0xFFFF)
delay about 1 second
write TwoMotorRumble.Stopped
~~~

Also prove:

- unavailable sink -> typed Unavailable / no write;
- first pulse write failure -> failed test and STOP still attempted where safe;
- STOP failure -> test is not reported as successful;
- no software percentage multiplication is applied;
- no second physical rumble writer is constructed.

Use an injected delay in tests instead of waiting one real second.

## 28. Expected production files

Likely additions:

~~~text
src/SteamInputAddonforClaw/Devices/MSI/Claw/
    MsiClawVibrationStrengthClient.cs
    MsiClawVibrationProfileCommand.cs
~~~

Likely modifications:

~~~text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
~~~

Do not modify AppSettings.cs / SettingsStore.cs merely to persist this value.

Touch additional files only when latest-main composition genuinely requires it.

## 29. Explicitly out of scope

Do not include:

- software rumble gain;
- forcing firmware ceiling to 100%;
- hard-coded 50% or 100% initial values;
- per-game vibration strength;
- automatic profile changes on game launch;
- separate values for Xbox360 vs SteamDeck;
- joystick LED;
- CTW integration;
- HHC integration;
- Center M process automation;
- generic XInput-slot rumble broadcast;
- automatic continuous firmware reconciliation;
- firmware write on every Slider tick;
- generic EEPROM editor;
- complete MSI controller-profile writer;
- a new controller authority;
- a new rumble authority;
- a new settings/persistence system.

## 30. Hardware acceptance test

Perform on a supported MSI Claw in Addon Controller Mode.

Start from a known MSI firmware state, preferably verify the current device's observed Center M 50% / 50% state first.

### Initial read

~~~text
firmware LM=50
firmware RM=50
open Controller page
-> Left=50%
-> Right=50%
~~~

Do not pre-write anything simply because the page opened.

### Independent Left

~~~text
move Left 50 -> 70
keep Right 50
-> no repeated writes while dragging
-> about 500 ms after settling, one Left mutation transaction
-> readback Left=70 / Right=50
-> Left Test vibrates only Left for about 1 second
-> STOP confirmed
~~~

### Independent Right

Equivalent test with Right only.

### Zero

Set one channel to 0, verify readback 0, and verify its Test produces no meaningful motor movement while the opposite channel remains untouched.

### Restart

Restart the Addon/Windows as appropriate:

~~~text
open Controller page again
-> values come from firmware
-> exact previously committed Left/Right pair is shown
~~~

### External change

Change LM/RM through MSI Center M or another already-verified firmware writer while stock authority is active, then return to the Addon-supported lifecycle and capture again.

Expected:

~~~text
Addon UI shows the actual changed firmware values
~~~

No stale Addon preference may overwrite them.

### PnP / resume sanity

After a normal sleep/resume or controller re-enumeration:

- Controller page refresh resolves the current command HID;
- no stale path is used;
- values are read correctly or reported unavailable;
- no background firmware rewrite occurs.

## 31. Validation

Run the normal current-main verification.

At minimum:

~~~text
Release build succeeds
full SteamInputAddonforClaw.Tests suite passes
frontend protocol/transport tests pass
controller UI tests pass
new vibration profile/client tests pass
new physical Test pulse tests pass
existing Full1902 production rumble tests pass
existing suspend/resume tests pass
existing physical ownership / PnP recovery tests pass
existing HidHide / VIIPER teardown tests pass
~~~

No regression is acceptable in the existing Xbox360 or SteamDeck production rumble paths.

## 32. Final invariants

After this PR:

~~~text
Controller / Vibration Strength
-> reads actual MSI LM/RM firmware
-> shows Left and Right independently
-> no fabricated default
-> no settings.json copy
~~~

~~~text
slider interaction
-> UI draft only
-> 500 ms settle
-> bounded firmware transaction
-> SyncToROM
-> exact readback
-> UI converges to actual firmware
~~~

~~~text
Test
-> existing Full1902 physical rumble sink
-> one selected channel at full runtime magnitude
-> firmware ceiling determines felt strength
-> about 1 second
-> finally STOP
~~~

And the existing Full1902 product invariant remains unchanged:

> Controller authority, PID1902 ownership, HidHide isolation, VIIPER presentation ownership, production rumble delivery, suspend/resume, and teardown remain owned by the existing Full1902 lifecycle. Vibration Strength adds one narrow firmware-backed device setting and no second authority.
