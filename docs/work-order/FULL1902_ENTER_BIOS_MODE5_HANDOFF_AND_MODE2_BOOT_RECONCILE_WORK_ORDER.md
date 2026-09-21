# Work Order — Full1902 Enter BIOS Mode 5 Handoff and Mode 2 Boot Reconcile

## Status

Corrective implementation and hardware-validation work order.

Current main baseline:

~~~text
0d17c3196ba7067266ceda70608b1e94d0d379ed
~~~

Authority order:

1. docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
2. docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
3. docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
4. older work orders only where they do not conflict with the current documents or code.

The app is standalone. Do not reintroduce CTW integration.

This work order supersedes the Enter-BIOS-specific PID1901/XInput assumptions in FULL1902_ENTER_BIOS_FIRMWARE_RESTART_WORK_ORDER.md. PR565's UAC-first helper design remains valid.

---

## 1. Problem

Current Enter BIOS does:

~~~text
UAC helper authorization
→ retire presentation
→ stop DirectInput
→ PID1902 / DInput → PID1901 / XInput
→ prove PID1901/XInput
→ shutdown.exe /r /fw /t 0
~~~

Real hardware validation disproved the assumption that PID1901/XInput is the BIOS controller state.

Known real behavior:

- the built-in MSI Claw controller can normally navigate BIOS;
- the current Addon Enter BIOS path reached PID1901/XInput successfully;
- after firmware reboot, the built-in controller still did not work in BIOS.

Therefore PID1901/XInput is not sufficient evidence of the firmware state required by BIOS.

MSI's controller protocol has a distinct firmware GamepadMode BIOS value:

~~~text
0 Offline
1 XInput
2 DInput
3 MSI
4 Desktop
5 BIOS
6 TESTING
~~~

The working hardware hypothesis for this PR is:

~~~text
normal Full1902 Windows state
GamepadMode 2 / DirectInput
PID1902
        ↓
Enter BIOS
        ↓
verified GamepadMode 5 / BIOS
        ↓
firmware restart
        ↓
BIOS controller works
        ↓
Windows boot
        ↓
Full1902 converges to GamepadMode 2 / PID1902
~~~

Do not encode any new PID assumption for Mode 5. Capture the actual topology from hardware validation.

---

## 2. Protocol facts

Existing MSI Center M RE establishes the switch frame:

~~~text
0F 00 00 3C 24 <GamepadMode> <MKeysFunction>
~~~

BIOS mode with Macro M-key behavior:

~~~text
0F 00 00 3C 24 05 00
~~~

The readback protocol is:

~~~text
0x24 = SwitchMode
0x26 = ReadGamepadMode
0x27 = GamepadModeAck
~~~

The vendor channel uses 64-byte reports:

~~~text
outbound report id = 0x0F
inbound report id  = 0x10
byte 4             = opcode
byte 5+            = arguments
~~~

A software 0x24 write is not sufficient proof. Explicitly send ReadGamepadMode 0x26 and require the matching GamepadModeAck 0x27.

Do not call SyncToROM / opcode 0x22. BIOS mode is a transient handoff and must not be flashed as a persistent profile state.

Reference evidence:

- MSI API_ControlMode.dll RE: GamepadMode includes BIOS=5.
- Handheld Companion MSI Claw source independently exposes BIOS=5, SwitchMode=0x24, ReadGamepadMode=0x26, GamepadModeAck=0x27.
- ClawConfigurator hardware notes independently document the 0x24/0x26/0x27 vendor-HID protocol and the requirement to query after a software switch.

### 2.1 Fork CTW hardware evidence — PID and firmware GamepadMode are not the same state

The current forked CTW source at `onehoon/ClawTweaks-Dev`, branch `release/v0.3.98.0`, already contains on-device firmware-mode readback and a directly relevant real-hardware result.

Relevant files:

~~~text
XboxGamingBarHelper/Devices/MSIClaw/MSIClawHidController.cs
XboxGamingBarHelper/Labs/ClawButtonMonitor.cs
reverse_engineered/decompiled/API_ControlMode_v1.0.2608.3101.cs
~~~

`MSIClawHidController.TryReadGamepadMode()` sends:

~~~text
0F 00 00 3C 26 ...
~~~

then reads bounded input reports and accepts only:

~~~text
byte[4] == 0x27
mode = byte[5]
~~~

The CTW source explicitly records this request/response as verified on a real MSI Claw:

~~~text
request  { 0F 00 00 3C 26 }
response { 10 00 00 3C 27 <mode> ... }
~~~

Its implementation uses a bounded 300 ms read timeout and scans up to four reports so unrelated input reports do not get mistaken for the mode acknowledgement.

More importantly, CTW's real-hardware `ClawButtonMonitor.ExitHwMouseMode()` documents and uses this transition:

~~~text
GamepadMode 4 / Desktop
PID1902
        ↓
SwitchMode(2)
        ↓
ReadGamepadMode verifies 2
        ↓
GamepadMode 2 / DirectInput
PID1902
~~~

The CTW source explicitly states that Desktop mode 4 and DirectInput mode 2 are both PID1902 on tested hardware and that the direct `4 → 2` transition does not re-enumerate the USB device.

This is decisive for the Addon design:

> **PID1902 does not prove GamepadMode 2.**

Therefore the Full1902 boot fast path must not use PID1902 alone as proof that the controller firmware is already in DirectInput mode once BIOS mode support exists. Firmware GamepadMode readback is a distinct current-world fact.

### 2.2 MSI original implementation confirms switch-then-readback ordering

The decompiled MSI `API_ControlMode_v1.0.2608.3101.cs` contains:

~~~csharp
public void SwitchGamepadMode(GamepadMode mode, MKeysFunction mKeysFunction)
{
    CommandQueue.Instance.Push(
        DeviceInfo,
        DeviceMessage.Immediate(
            CommandType.SwitchMode,
            (byte)mode,
            (byte)mKeysFunction));

    Thread.Sleep(20);
    ReadCurrentMode();
}
~~~

and:

~~~csharp
public void ReadCurrentMode()
{
    CommandQueue.Instance.Push(
        DeviceInfo,
        DeviceMessage.Immediate(CommandType.ReadGamepadMode));
}
~~~

The MSI response handler treats `GamepadModeAck` as authoritative state and updates:

~~~text
DeviceState.GamepadMode
DeviceState.MKeysFunction
~~~

before raising the mode-updated event.

The implementation lesson for this PR is therefore:

~~~text
SwitchMode
→ short settle matching MSI's observed 20 ms ordering
→ ReadGamepadMode
→ require GamepadModeAck
~~~

Do not infer success merely from the output write.

Do not overstate the RE: the MSI source proves command ordering and readback, but it does **not** by itself prove that the switch and read commands share one persistent HID handle. The Addon may use one short-lived session when safe, or re-resolve the same verified command interface if the handle becomes invalid, while preserving the same strong-identity rules.

---

## 3. Goal

Replace the Full1902 Enter BIOS controller sequence:

~~~text
PID1902 / DInput
→ PID1901 / XInput
→ firmware restart
~~~

with:

~~~text
PID1902 / DInput
→ verified firmware GamepadMode 5
→ firmware restart
~~~

Then on the next Center M Disabled Windows boot converge back to:

~~~text
GamepadMode 2 / DirectInput
PID1902
DirectInput acquired
HidHide baseline verified
VIIPER presentation restored
~~~

Do this from current hardware facts. Do not persist EnteringBios, ReturnFromBios, previous mode, boot epoch, or a one-shot recovery marker.

---

## 4. Full1902 authority must not change

Center M Disabled still means:

~~~text
Addon Runtime = controller authority
normal Windows GamepadMode desired = 2 / DirectInput
normal Windows physical PID desired = PID1902
persistent HidHide authority = Addon
mandatory Runtime startup = unchanged
Win+G suppression authority = unchanged
~~~

Enter BIOS is not:

- a Center M authority transfer;
- a reason to enable Center M roots;
- a reason to clear HidHide;
- a reason to remove mandatory startup;
- a reason to disarm Win+G suppression;
- a reason to call the stock authority restoration core.

Normal Windows reboot is still not an authority-release boundary.

---

## 5. Preserve PR565 UAC-first ordering

Required order:

~~~text
confirmation
→ elevate firmware helper
→ helper Ready
→ controller mutation
~~~

If UAC is cancelled, helper launch fails, or Ready is not obtained:

~~~text
0 presentation teardown
0 DirectInput stop
0 controller mode write
0 restart request
~~~

The elevated helper stays narrow. It only performs:

~~~text
Ready handshake
wait for RestartFirmware
shutdown.exe /r /fw /t 0
exit
~~~

Do not move MSI HID operations into the elevated helper. Do not add /f.

---

## 6. New Enter BIOS flow for Center M Disabled / Full1902

Required sequence:

~~~text
1. capture Center M state
2. lower-level Runtime safety check
3. pre-authorize firmware restart helper and require Ready
4. retire front-button callbacks targeting the presentation
5. neutralize and retire virtual presentation / VIIPER
6. enter the existing physical-owner gate
7. stop the process-owned DirectInput session
8. resolve the same strongly verified MSI Claw command HID
9. send SwitchMode(BIOS=5)
10. send ReadGamepadMode(0x26)
11. require GamepadModeAck == 5
12. only now mark deliberate firmware-restart release
13. send RestartFirmware to the already-authorized helper
14. helper runs shutdown.exe /r /fw /t 0
~~~

No Enter BIOS path may call:

~~~text
ReleaseToXInputAsync(... firmwareRestart: true)
SwitchModeAsync(XInput)
StockCenterMStartupBaseline.EstablishAsync
PID1901 proof
XInput proof
~~~

The normal Enable Center M and Restart path remains unchanged and still restores verified PID1901/XInput.

Keep the Addon-owned HidHide baseline unchanged through the BIOS handoff.

Do not assume Mode 5 maps to PID1901, PID1902, or PID1903. Mode readback is the success criterion. PID/topology is diagnostic evidence for this first hardware validation.

---

## 7. Separate firmware GamepadMode from native PID mode

Current MsiClawNativeMode describes the existing native Windows topology:

~~~csharp
XInput
DirectInput
Other
~~~

Do not add BIOS to MsiClawNativeMode just to carry byte 5.

Introduce the smallest typed protocol representation, for example:

~~~csharp
internal enum MsiClawGamepadMode : byte
{
    Offline = 0,
    XInput = 1,
    DirectInput = 2,
    Msi = 3,
    Desktop = 4,
    Bios = 5,
    Testing = 6,
}
~~~

Existing XInput/DInput native-mode logic may map to the corresponding firmware values, but the established PID1901/PID1902 transition logic must otherwise stay intact.

This is a protocol enum, not a new authority or manager.

---

## 8. Extend the existing vendor-HID primitives narrowly

Reuse:

~~~text
MsiClawModeCommand
WindowsMsiClawModeWriter
WindowsMsiClawRawHidTransport
existing exact command-HID resolution
existing strong physical identity checks
~~~

Add only the capability required for:

~~~text
Build SwitchMode(GamepadMode)
Build ReadGamepadMode
bounded read of one matching GamepadModeAck
parse and validate mode byte
~~~

A reasonable shape is:

~~~csharp
MsiClawModeCommand.BuildSwitch(MsiClawGamepadMode mode)
MsiClawModeCommand.BuildReadGamepadMode()
MsiClawModeCommand.TryParseGamepadModeAck(...)
~~~

Existing Build(MsiClawNativeMode) may remain and delegate to the typed firmware builder.

Readback must use the exact verified control HID. Do not accept the first generic VID_0DB0 HID interface.

Follow the firmware's observed command ordering:

~~~text
resolve the exact strongly verified command HID
→ write SwitchMode 0x24
→ short settle; MSI reference implementation uses 20 ms
→ write ReadGamepadMode 0x26
→ read until matching GamepadModeAck 0x27 or bounded timeout
→ close
~~~

Prefer keeping the operation local and short-lived. If the same opened command-HID handle remains valid, it may be reused for the write/query/read sequence. If the firmware switch invalidates that handle, re-resolve only the same strongly verified physical MSI Claw command interface and continue the bounded readback. Do not broaden matching to an arbitrary VID_0DB0 interface.

Use CTW's proven readback behavior as a practical reference:

~~~text
ReadTimeout = 300 ms
skip unrelated reports
accept only opcode 0x27
bounded report attempts
~~~

The exact timeout/attempt constants may be adapted to the Addon's async/native transport, but the operation must remain bounded. Do not create a polling service or a long-lived firmware-mode watcher.

---

## 9. Physical owner change

The existing ReleaseForFirmwareRestartAsync currently delegates to the XInput release path. Replace that Enter-BIOS-specific behavior with an explicit BIOS handoff, for example:

~~~csharp
Task<PhysicalOwnershipReleaseResult> PrepareForFirmwareBiosAsync(
    CancellationToken cancellationToken);
~~~

Under the existing physical-owner gate:

1. stop DirectInput if owned;
2. clear the live process-owned input publication;
3. capture the stable current physical state;
4. require the same strong MSI Claw identity;
5. resolve the exact current command HID;
6. send GamepadMode Bios;
7. query and require mode 5;
8. only after mode 5 is verified set the existing deliberate firmware-restart release guard.

Do not set the deliberate firmware-release guard before Mode 5 is proven. That lets normal recovery remain available when the attempted BIOS handoff did not actually commit.

The existing owner gate already serializes real recovery. Do not add another lock, epoch, or barrier.

---

## 10. Next Disabled Windows boot: Mode 5 → Mode 2

Do not add a persisted BIOS-return flag.

### PID1901 return

If the device returns as PID1901:

~~~text
existing Full1902 acquisition
→ existing verified PID1901 → PID1902 / DirectInput transition
→ normal acquire
~~~

No new boot state is required.

### PID1902 return

A PID1902 device may no longer be blindly assumed to mean firmware mode 2 after BIOS mode exists as an independent firmware fact.

This is not theoretical. Fork CTW hardware validation already proved that `GamepadMode.Desktop = 4` and `GamepadMode.DirectInput = 2` can both exist under PID1902, and CTW must query `ReadGamepadMode` to distinguish them. Treat BIOS mode 5 the same way: PID and firmware mode are separate facts.

Before accepting an already-present PID1902 as already DirectInput, perform the narrow mode query when the exact command HID is safely resolvable.

Required behavior:

~~~text
PID1902 + mode query == 2
→ existing fast path
→ no redundant mode write

PID1902 + mode query == 5
→ send SwitchMode(2)
→ ReadGamepadMode
→ require Ack == 2
→ continue existing DirectInput/HidHide acquisition

PID1902 + mode query unavailable
→ do not invent BIOS state
→ preserve current acquisition behavior
→ existing DirectInput readiness remains the authority
~~~

This PR should solve the proven BIOS-return 5 → 2 case only. Do not turn every possible firmware mode into a new recovery state machine.

If real validation shows Mode 5 returns under another PID/topology, capture that evidence and adjust the smallest current-state resolver in the implementation. Do not guess ahead of the hardware test.

---

## 11. Failure policy

### Before controller mutation

The following leave the current Full1902 controller untouched:

~~~text
UAC cancelled
helper missing
helper launch failure
helper Ready timeout
Center M state unavailable/partial
Runtime safety block
~~~

### Mode 5 write/readback failure

After UAC approval and controlled teardown:

~~~text
Mode 5 write/readback fails
→ do not request firmware restart
→ fail closed
→ log exact write/readback reason
~~~

A successful WriteFile alone is not success.

Use an already-available small recovery only if current Mode 2 can be proven and the existing owner can be safely reacquired. Do not add a broad rollback state machine for theoretical cases.

### Firmware restart failure after verified Mode 5

This is a real operation failure. Log clearly that Mode 5 had already been verified.

If a small, proven Mode 5 → Mode 2 rollback naturally fits the existing owner, it is acceptable. Otherwise fail closed and let the next normal Runtime start converge to Mode 2.

Do not add new authority/state machinery solely to cover unlikely instruction-level races.

---

## 12. UI copy

Delete:

~~~text
The controller will temporarily switch to XInput so it can be used in BIOS.
~~~

Use:

~~~text
Enter BIOS?

The device will restart directly into BIOS settings.

Save your work before continuing.

[Cancel] [Restart and Enter BIOS]
~~~

Cancel remains the default button.

Do not expose Mode 5, PID numbers, vendor HID, UEFI, or protocol terminology in normal UI.

---

## 13. Hardware-validation logging

Add concise logs sufficient to prove the real controller lifecycle.

Before handoff:

~~~text
Event=EnterBiosGamepadModePrepareStarted
CenterMState
CurrentNativeMode
CurrentPid
PhysicalIdentityConfidence
~~~

Mode operation:

~~~text
Event=GamepadModeQueryCompleted
ObservedGamepadMode=<value>

Event=EnterBiosBiosModeWriteCompleted
RequestedGamepadMode=5
WriteSucceeded=<bool>

Event=EnterBiosBiosModeVerified
ObservedGamepadMode=5
~~~

Next Disabled boot:

~~~text
Event=DisabledBootGamepadModeObserved
ObservedGamepadMode=<value>
CurrentPid=<pid>

Event=DisabledBootBiosModeRestoreStarted
FromGamepadMode=5
ToGamepadMode=2

Event=DisabledBootBiosModeRestoreVerified
ObservedGamepadMode=2
~~~

Capture the current MSI PID/control-HID topology around Mode 5 if it can be done without delaying or destabilizing reboot. Treat it as evidence, not as a hardcoded expectation.

No high-frequency steady-state firmware-mode polling.

---

## 14. Tests

### Protocol tests

Prove exact BIOS switch bytes:

~~~text
0F 00 00 3C 24 05 00
~~~

zero-padded to 64 bytes.

Prove ReadGamepadMode uses opcode 0x26.

Prove parsing of an inbound 0x27 acknowledgement containing mode 5.

Also prove the practical readback behavior mirrored from the fork CTW evidence:

- unrelated input reports before the 0x27 acknowledgement are ignored;
- a matching 0x27 acknowledgement returns byte 5 as GamepadMode;
- the read loop is bounded;
- timeout/no acknowledgement is failure, not success-by-write.

Reject wrong report id, wrong opcode, truncated responses, and invalid mode values where a known mode is required.

### Enter BIOS ordering

For Full1902 Disabled:

~~~text
firmware authorization
→ presentation retirement
→ physical BIOS-mode prepare
→ verified Mode 5
→ firmware restart
~~~

Prove:

- UAC cancellation causes zero controller mutation.
- Mode 5 failure causes zero firmware restart calls.
- Enter BIOS no longer expects PID1901.
- Enter BIOS no longer calls stock-baseline establishment.
- HidHide is not cleared.
- Center M roots are not changed.
- mandatory startup is not removed.
- Win+G authority is not disarmed.

### Existing stock restoration

Keep existing tests proving Enable Center M and Restart still does:

~~~text
verified PID1901/XInput
→ HidHide release
→ Center M roots enabled
→ restart
~~~

### Disabled boot

Add:

~~~text
PID1902 + firmware mode 2
→ no redundant mode write
→ normal acquire

PID1902 + firmware mode 5
→ write mode 2
→ query verifies mode 2
→ normal acquire

PID1902 + mode query unavailable
→ existing acquisition path remains available

PID1901
→ existing PID1901 → PID1902 transition remains authoritative
~~~

Do not add artificial scheduler-interleaving tests.

---

## 15. Manual hardware validation — required

### Test A: BIOS handoff

Start from healthy Center M Disabled Full1902:

~~~text
PID1902
GamepadMode 2
DirectInput live
virtual presentation live
~~~

Then:

1. Enter BIOS.
2. Accept UAC.
3. Confirm 0x24 Mode 5 write in logs.
4. Confirm explicit 0x26 → 0x27 readback reports mode 5.
5. Confirm firmware restart is requested only after verification.
6. Enter BIOS.
7. Verify built-in controller navigation works.

This is the primary acceptance test.

### Test B: Windows return

Exit BIOS and boot Windows.

Verify:

1. mandatory Runtime starts;
2. current PID and GamepadMode are logged;
3. if Mode 5 persisted under PID1902, Runtime performs 5 → 2;
4. mode 2 readback succeeds;
5. PID1902 / DirectInput ownership is established;
6. HidHide baseline is healthy;
7. VIIPER presentation returns;
8. no double input;
9. WING/OEM behavior is normal.

### Test C: UAC cancel

Cancel UAC and verify:

~~~text
PID1902 remains
GamepadMode remains 2
DirectInput remains live
presentation remains live
no restart
~~~

### Test D: repeatability

Run at least three complete cycles:

~~~text
Mode 2
→ Mode 5
→ BIOS
→ Windows
→ Mode 2
~~~

Capture actual PID/topology before Mode 5, on the BIOS-return boot, and after Mode 2 recovery.

---

## 16. Likely files

Primary expected files:

~~~text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeCommand.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawModeWriter.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/WindowsMsiClawRawHidTransport.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs
src/SteamInputAddonforClaw/CenterMStartup/CenterMRebootAuthorityTransition.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs
tests/SteamInputAddonforClaw.Tests/MsiClawModeSwitchTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs
tests/SteamInputAddonforClaw.Tests/CenterMRebootAuthorityTransitionTests.cs
~~~

Additional focused tests are acceptable if they fit current repository organization.

Do not perform a broad controller-stack rename/refactor.

---

## 17. Retire the incorrect Enter BIOS coupling

After the new path is implemented, remove Enter-BIOS-specific use of:

~~~text
ReleaseForFirmwareRestartAsync → ReleaseToXInputAsync
EnterBiosPid1901Verified
temporary PID1901/XInput success wording
StockCenterMStartupBaseline from Enter BIOS
the XInput BIOS confirmation text
~~~

Do not delete stock/XInput primitives still required by Enable Center M, uninstall stock restoration, or other legitimate stock-authority paths.

---

## 18. Overengineering guardrails

Do not add:

- generalized firmware-mode manager;
- second controller authority;
- persisted BIOS-return state;
- mode journal;
- boot epoch/barrier;
- Windows service;
- privileged hardware broker;
- continuous mode polling;
- broad HID wildcard ownership;
- defensive synchronization for theoretical instruction-level races.

The required lifecycle is only:

~~~text
UAC approval
→ verified Mode 5 handoff
→ firmware restart
→ next Disabled boot verified Mode 2 convergence
~~~

Reuse the existing owner, gate, and reconcile structure.

---

## 19. Acceptance criteria

1. Full1902 Enter BIOS no longer switches to PID1901/XInput.
2. UAC authorization completes before any controller mutation.
3. Runtime sends exact SwitchMode(BIOS=5).
4. Mode 5 is explicitly verified with ReadGamepadMode 0x26 → GamepadModeAck 0x27.
5. Firmware restart is requested only after Mode 5 verification.
6. Center M roots, HidHide, mandatory startup, and Full1902 authority remain unchanged.
7. No persisted BIOS-return marker is introduced.
8. Next Disabled boot converts PID1902 + firmware Mode 5 to verified Mode 2 before normal DirectInput ownership continues.
9. The implementation does not equate PID1902 with firmware Mode 2; this is covered by tests reflecting the CTW-proven PID1902 Mode4/Mode2 distinction.
10. PID1901 return still uses the existing verified PID1901 → PID1902 path.
11. Enable Center M and Restart remains unchanged and still restores verified PID1901/XInput.
12. UI no longer claims XInput is the BIOS controller mode.
13. Real hardware validation confirms the built-in controller works inside BIOS after Mode 5 handoff.
14. Real hardware validation confirms healthy Full1902 Mode 2 / PID1902 operation after returning to Windows.
15. Debug and Release builds/tests pass.
16. No new architecture exists solely for theoretical race defense.
