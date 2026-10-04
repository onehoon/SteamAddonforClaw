# Work Order — Full1902 CG3EM Vibration 0/100 Runtime Write Probe

> Date: 2026-10-04
> Repository baseline reviewed: main at 550190b4129ca96cffa13afb27587c5d7d8641d7
> Scope: one focused developer-diagnostic PR
> Target: MSI Claw 8 AI+ EX CG3EM / MS-1T91 / controller firmware 0x0419
> Safety: bounded diagnostic mutation only; production vibration-strength mutation remains fail-closed

## 1. Goal

Perform the smallest hardware experiment that can prove or disprove whether MSI Center M's profile-write contract at serialized motor offsets 0x22/0x23 changes the physical Left/Right vibration strength on CG3EM firmware 0x0419.

The diagnostic writes one deliberately asymmetric pair:

~~~text
Left  = 0
Right = 100
~~~

using the Center M-compatible contiguous profile write:

~~~text
0F 00 00 3C 21 01 00 22 02 00 64 ...
~~~

After the write succeeds, the user manually uses the existing Controller-page physical Test buttons:

~~~text
Left Test  -> expected: effectively no vibration
Right Test -> expected: maximum/strong vibration
~~~

The user should press Left Test exactly once and Right Test exactly once for the first acceptance run.

The diagnostic must NOT issue SyncToROM command 0x22.

After testing, provide an explicit developer-only restore action that writes:

~~~text
Left  = 50
Right = 50
~~~

with the same contiguous runtime write shape and again without SyncToROM.

This PR is only an evidence-gathering probe. It must not enable normal Vibration Strength mutation on CG3EM.

---

## 2. Evidence and reason for the probe

### 2.1 Center M runtime log

MSI_Center_M_Server_ControlMode_2026-10-04.log proves Center M sends slider values into ControlMode and requests profile saves.

Observed example:

~~~text
SyncToMotors|{"LeftMotorValue":50,"RightMotorValue":100}
Motors: Left=50 Right=100
Save To Profile
~~~

Later it returned to:

~~~text
SyncToMotors|{"LeftMotorValue":50,"RightMotorValue":50}
Motors: Left=50 Right=50
Save To Profile
~~~

### 2.2 Center M values are not established as durable hardware state

Field observation on the target unit:

~~~text
change Center M vibration away from 50/50
-> fully exit Center M from tray
-> relaunch Center M
-> vibration UI returns to 50/50
~~~

Therefore Center M's slider values are not proven to be durable EEPROM/hardware source-of-truth values.

The product should not assume persistence from the UI or Save To Profile log.

### 2.3 Static Center M RE

Current RE proves:

~~~text
MotorModule.LeftMotorValue  -> serialized profile relative offset 0x22
MotorModule.RightMotorValue -> serialized profile relative offset 0x23
~~~

When both bytes change in a cached profile, Center M can emit:

~~~text
WriteProfile
index   = 1
offset  = 0x22
length  = 2
payload = LM, RM
~~~

Example shape:

~~~text
0F 00 00 3C 21 01 00 22 02 <LM> <RM> ...
~~~

This is a serialized-profile write contract, not proof of absolute EEPROM addresses.

### 2.4 PR #660 readback remains ambiguous

On CG3EM firmware 0x0419 the read-only probe showed:

~~~text
request index 0 / offset 0x22 / len 2
-> response index 1
-> 50/50

request index 1 / offset 0x22 / len 2
-> response index 1
-> 50/50
~~~

Repeated Center M manipulation did not change those short-read results.

Therefore this PR must validate physical effect directly and must not depend on readback.

---

## 3. Preserve Full1902 authority

Read and preserve:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/RE_MSI_ControllerVibration.md

The authority contract remains:

~~~text
Center M Enabled
-> MSI / stock authority
-> desired physical PID1901

Center M Disabled
-> Addon Runtime authority
-> desired physical PID1902
-> Addon-owned DirectInput
-> HidHide
-> VIIPER presentation
~~~

This diagnostic must not alter controller ownership, HidHide, VIIPER, presentation selection, sleep/resume policy, restart behavior, or PID restoration.

---

## 4. Production vibration mutation remains fail-closed

Keep MsiClawVibrationFirmwarePolicy unchanged.

Normal MsiClawVibrationStrengthClient.SetAsync must continue to reject CG3EM before any write.

Do not special-case 0/100 inside production SetAsync.

Do not enable the normal Controller-page strength sliders.

Do not mark msi.claw.cg3em as verified for production.

The 0/100 experiment must have a separate explicitly named developer-diagnostic path.

---

## 5. Add an exact contiguous pair-write builder

The current BuildWriteProfile(address, percent) emits a one-byte profile write.

Do not call it twice for this experiment.

Add a narrow diagnostic helper such as:

~~~csharp
internal static byte[] BuildDiagnosticMotorPairWrite(
    int leftPercent,
    int rightPercent)
~~~

Validate each value as 0..100.

The report must be exactly 64 bytes and use:

~~~text
report[0]  = 0x0F
report[1]  = 0x00
report[2]  = 0x00
report[3]  = 0x3C
report[4]  = 0x21
report[5]  = 0x01
report[6]  = 0x00
report[7]  = 0x22
report[8]  = 0x02
report[9]  = Left
report[10] = Right
~~~

Main probe expected prefix:

~~~text
0F-00-00-3C-21-01-00-22-02-00-64
~~~

Restore expected prefix:

~~~text
0F-00-00-3C-21-01-00-22-02-32-32
~~~

This helper must not append or trigger BuildSyncToRom.

---

## 6. Add a developer-only runtime write probe

Place the behavior in MsiClawVibrationStrengthClient or the nearest existing vibration profile owner.

Preferred conceptual operation:

~~~text
RunDiagnosticMotorPairWriteAsync(left, right, centerMIsExactlyDisabled, token)
~~~

This must be separate from production SetAsync.

Before any write require:

1. model exactly msi.claw.cg3em;
2. Center M startup authority exactly Disabled;
3. current command HID resolves uniquely;
4. strong physical identity;
5. physical ProductId = 0x1902;
6. exact PID1902 control endpoint:
   - UsagePage 0xFFF0
   - Usage 0x0040.

On any failed precondition:

~~~text
return Unavailable/Failed
zero writes
~~~

Do not switch PID.
Do not start/stop Center M.
Do not add retries.

---

## 7. Diagnostic write semantics

For this PR, transport success is the only programmatic success criterion.

Sequence:

~~~text
resolve exact current PID1902 command HID
-> emit exactly one 0x21 contiguous pair write
-> report transport result
~~~

Do not:

- issue SyncToROM;
- issue automatic readback;
- require PR #660 readback to match;
- call production CommitChannelAsync;
- write Left and Right separately.

The human physical motor test is the validation signal.

---

## 8. Developer UI only

Keep the probe out of normal product UI.

Use the existing Developer Menu -> Vibration Test page.

Add a separate section:

~~~text
Vibration Profile 0/100 Probe

[Apply Left 0 / Right 100]

After Apply succeeds:
1. Open Controller > Vibration Strength.
2. Press Left Test once.
3. Press Right Test once.
4. Return here and press Restore 50 / 50.

[Restore 50 / 50]
~~~

Show a clear warning:

~~~text
Developer-only physical hardware mutation.
No SyncToROM is sent.
~~~

Apply/Restore buttons must be disabled while an operation is running.

Do not auto-run on page activation, Addon startup, Controller-page activation, resume, or PnP arrival.

Explicit user action is required.

---

## 9. Existing Left/Right Test buttons are the measurement tool

Do not add a second rumble implementation.

The user uses the existing Controller-page Left Test and Right Test.

Those tests already use the production physical rumble sink and mandatory one-second STOP behavior.

After Apply 0/100:

### Left Test

Expected:

~~~text
full Left-only test request
-> effective physical result: silent or clearly near-zero
~~~

### Right Test

Expected:

~~~text
full Right-only test request
-> effective physical result: strong / maximum
~~~

For the first run the user presses:

~~~text
Left Test once
Right Test once
~~~

Do not loop.
Do not extend pulse duration.
Do not change STOP behavior.

---

## 10. Explicit Restore 50/50 is required

Because the experiment intentionally disables one motor, provide a Restore 50/50 action.

It sends exactly one contiguous pair write:

~~~text
0F 00 00 3C 21 01 00 22 02 32 32 ...
~~~

No SyncToROM.

Use the same model, authority, identity, PID and endpoint checks as Apply.

Do not claim 50/50 is a universal MSI default.

For this probe it is the observed CG3EM baseline and the value Center M shows again after full restart on the target test unit.

If restore transport fails:

- show a visible developer warning;
- write an Info/Warn log;
- do not retry automatically.

---

## 11. Frontend transport

Do not route this through SetControllerVibrationStrengthAsync because that is the production mutation contract.

Expose the smallest dedicated developer RPC, for example:

~~~text
RunControllerVibrationProfileWriteProbe
Mode = ApplyZeroHundred | RestoreFiftyFifty
~~~

Do not expose arbitrary address, index, opcode, length, or percentages through this developer RPC.

If the named-pipe contract changes, bump FrontendTransportProtocol.CurrentVersion exactly once and update protocol tests.

Do not create a generic profile-write API.

---

## 12. Logging

Use Info-level events.

Apply example:

~~~text
ControllerVibrationProfileWriteProbeStarted
Model=msi.claw.cg3em
ProductId=0x1902
ProfileIndex=1
Address=0x0022
Length=2
Left=0
Right=100
SyncToRom=False
VerifiedForProduction=False
~~~

Completion:

~~~text
ControllerVibrationProfileWriteProbeCompleted
Left=0
Right=100
TransportSucceeded=True
SyncToRom=False
VerifiedForProduction=False
~~~

Restore should log equivalent events with Left=50 Right=50.

Do not log entire 64-byte reports by default.

A bounded prefix is enough if needed for diagnostics.

Transport success must never be logged as physical validation.

---

## 13. Hardware acceptance procedure

### Preparation

Target:

~~~text
MSI Claw 8 AI+ EX CG3EM
MS-1T91
controller firmware 0x0419
physical PID1902
~~~

Before testing:

1. Center M startup authority is Disabled.
2. Completely exit any manually launched Center M process/tray instance.
3. Close games and other rumble tools.
4. Start the Addon normally and confirm Full1902 PID1902 ownership is healthy.

### Apply

Open:

~~~text
Settings
-> Developer Menu
-> Vibration Test
~~~

Press:

~~~text
Apply Left 0 / Right 100
~~~

Confirm transport success.

### Physical comparison

Open:

~~~text
Controller
-> Vibration Strength
~~~

Press Left Test exactly once.

Then press Right Test exactly once.

Record the physical result.

### Restore

Return to Developer Menu -> Vibration Test.

Press:

~~~text
Restore 50 / 50
~~~

Confirm transport success.

Optionally press Left Test and Right Test once more to confirm both motors are active again.

---

## 14. Result interpretation

### PASS

~~~text
After 0/100:
Left Test  -> silent / effectively zero
Right Test -> strong / maximum

After 50/50 restore:
Left Test  -> active
Right Test -> active
~~~

Interpretation:

The Center M-compatible profile write at index 1 / relative offset 0x22 / length 2 has a real live physical effect on CG3EM firmware 0x0419.

This is enough evidence for a later production design where Addon settings become source of truth and are reapplied when PID1902 ownership is acquired/reacquired.

It does not prove ROM persistence.

### Inverted/partial

If Left remains strong and Right becomes silent, or only one unexpected side changes:

- field ordering/motor mapping requires correction;
- keep production fail-closed.

### No effect

If both Test buttons behave as before:

- 0x21 pair write alone is insufficient for live gain;
- do not add SyncToROM in the same PR/test run;
- review the evidence first and prepare a separate follow-up if required.

---

## 15. Tests

### Command builder

Verify exact 64-byte output:

~~~text
BuildDiagnosticMotorPairWrite(0, 100)
-> 0F-00-00-3C-21-01-00-22-02-00-64 ...

BuildDiagnosticMotorPairWrite(50, 50)
-> 0F-00-00-3C-21-01-00-22-02-32-32 ...
~~~

Reject:

~~~text
-1 / 0
0 / -1
101 / 0
0 / 101
~~~

### Client diagnostic tests

Verify:

- CG3EM + exact strong PID1902 control HID + Center M Disabled -> exactly one write;
- no SyncToROM write follows;
- no automatic readback;
- transport failure -> Failed;
- non-CG3EM -> Unavailable and zero writes;
- PID1901 -> Unavailable and zero writes;
- weak/ambiguous identity -> Unavailable and zero writes;
- Center M not exactly Disabled -> Unavailable and zero writes;
- Restore sends one 50/50 pair write and no SyncToROM.

### Regression

Existing tests must still prove:

- normal SetAsync remains fail-closed for CG3EM;
- PR #660 read-only diagnostics unchanged;
- physical Left/Right Test uses the same rumble sink;
- one-second STOP guarantee unchanged;
- suspend/resume behavior unchanged;
- HidHide/VIIPER ownership unchanged;
- LED behavior unchanged.

### Frontend

If adding a developer RPC:

- validate request enum/mode;
- named-pipe client/server dispatch;
- protocol version bump if required;
- no arbitrary hardware-write parameters exposed.

---

## 16. Documentation

Update docs/RE_MSI_ControllerVibration.md with currently proven facts:

### PROVEN

~~~text
Center M logs SyncToMotors values such as 50/100 and Save To Profile.

On the target unit, fully exiting and relaunching Center M returns the UI to 50/50.

Center M's known contiguous LM/RM write shape is:
index 1 / relative offset 0x22 / length 2.
~~~

### UNKNOWN until hardware acceptance

~~~text
whether 0x21 pair write without SyncToROM changes live physical motor gain

whether the state is controller RAM, driver-side state, or another volatile profile layer

whether a later production implementation should reapply Addon settings on
startup/resume/PnP reacquire
~~~

Do not claim physical success from transport success alone.

---

## 17. Explicitly out of scope

Do not:

- enable production vibration sliders;
- change MsiClawVibrationFirmwarePolicy;
- call production SetAsync for the probe;
- issue SyncToROM;
- persist 0/100 as user settings;
- auto-apply at startup/resume/PnP;
- add software rumble attenuation;
- change XInput/VIIPER rumble scaling;
- change the physical rumble packet format;
- change pulse duration or STOP;
- switch PID;
- parse Center M profile.rec in production;
- integrate with Center M IPC;
- add generic profile writers;
- add retry/state-machine/manager abstractions;
- modify LED/TDP/fan/profile features.

---

## 18. Acceptance criteria

Implementation:

~~~text
explicit developer Apply 0/100
-> exact CG3EM/PID1902/strong-identity/Center-M-Disabled checks
-> exactly one contiguous 0x21 write
   index 1 / offset 0x22 / length 2 / payload 00 64
-> no SyncToROM
-> transport result logged
-> production vibration mutation still unavailable
~~~

Restore:

~~~text
explicit developer Restore 50/50
-> exactly one contiguous 0x21 write
   index 1 / offset 0x22 / length 2 / payload 32 32
-> no SyncToROM
~~~

Manual hardware acceptance:

~~~text
Apply 0/100
-> Left Test once
-> Right Test once
-> observe clear physical difference
-> Restore 50/50
~~~

Keep the PR simple: one bounded diagnostic writer, one explicit developer trigger surface, one restore action, no new authority abstraction.
