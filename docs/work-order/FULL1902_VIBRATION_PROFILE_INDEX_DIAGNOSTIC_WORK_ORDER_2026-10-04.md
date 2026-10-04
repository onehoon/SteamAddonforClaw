# Work Order — Full1902 Vibration Profile Index Read-Only Diagnostic

> Date: 2026-10-04  
> Repository baseline reviewed: \`main\` at \`a3d0ee9315b93afb584f85a67bb0b68f7c643e3e\`  
> Scope: one focused diagnostic PR  
> Affected feature: Controller > Vibration Strength  
> Product model: standalone Full1902 application  
> Safety mode: read-only device probe; production vibration-strength writes remain fail-closed

## 1. Goal

Resolve the remaining ambiguity in the MSI Claw vibration-strength profile contract without enabling production firmware writes.

Recent MSI Center M static RE established a new and important asymmetry:

~~~text
Center M whole-profile READ request
-> profile index 0

Center M profile WRITE request
-> profile index 1
~~~

The current SteamAddon diagnostic read is hard-coded to profile index \`1\`:

~~~csharp
report[4] = 0x04; // ReadProfile
report[5] = 0x01; // currently fixed index 1
~~~

and separately reads:

~~~text
index 1 / offset 0x22 / length 1
index 1 / offset 0x23 / length 1
~~~

This is not enough to prove that the Addon is reading the same profile state that Center M displays or persists.

The diagnostic PR must compare, on the same currently owned physical controller:

~~~text
ReadProfile index 0 / offset 0x22 / length 2
ReadProfile index 1 / offset 0x22 / length 2
~~~

and log the raw response structure for both requests.

Do not enable vibration-strength mutation from this PR.

---

## 2. Required architecture documents

Read and preserve the current Full1902 authority contract:

- \`docs/Full 1902 Implementation/README.md\`
- \`docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md\`
- \`docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md\`
- \`docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md\`

This work must not alter:

~~~text
Center M Enabled
-> MSI / stock authority
-> desired physical PID1901

Center M Disabled
-> Addon Runtime authority
-> desired physical PID1902
-> Addon-owned DirectInput / HidHide / VIIPER presentation
~~~

The diagnostic runs only through the existing exact MSI command-HID resolution path.

Do not introduce a new controller authority, mode switch, PnP cycle, HidHide policy, or VIIPER behavior.

---

## 3. Evidence from the latest MSI Center M RE

The following findings are the basis for this diagnostic.

### 3.1 Binary versions reviewed

The local RE inspected:

~~~text
UC_ControlMode.dll  1.0.2608.1201
API_ControlMode.dll 1.0.2606.2401
~~~

These versions do not match.

Therefore all Center M findings below are static behavior proven within the supplied binaries, but cross-assembly runtime compatibility must not be silently assumed.

### 3.2 UI values are local profile values

The Center M vibration sliders:

- are independent Left / Right values;
- have range \`0..100\`;
- initialize to \`50\` in the UI surface;
- use a roughly 300 ms debounce;
- synchronize on normal value-change/debounce and selected page-lifecycle boundaries.

The UI path is:

~~~text
UC_Vibration
-> ControlProfile.gamepadMotors
-> JSON MP.LM / MP.RM
-> API_ControlMode
-> MotorModule.LeftMotorValue / RightMotorValue
~~~

The values displayed when the page opens come from Center M's local control profile / \`profile.rec\`.

They are **not proven device readback values**.

Therefore:

~~~text
Center M UI says 30/70
!= proof that firmware/device readback is 30/70
~~~

### 3.3 Motor fields are serialized offsets

The serialized profile contains:

~~~text
MotorModule.LeftMotorValue  -> decimal 34 -> relative offset 0x22
MotorModule.RightMotorValue -> decimal 35 -> relative offset 0x23
~~~

The complete serialized profile is approximately 1,478 bytes in the inspected API binary.

This proves profile-relative layout.

It does not prove an absolute EEPROM-address interpretation independent of the profile command semantics.

### 3.4 Center M write request

When the cached profile already exists and only LM/RM change, Center M can produce a write shaped as:

~~~text
0F 00 00 3C
21
01
00 22
02
<LM> <RM>
...
~~~

Conceptually:

~~~text
WriteProfile
index  = 1
offset = 0x22
length = 2
payload = LM, RM
~~~

and then queue:

~~~text
0F 00 00 3C 22
~~~

for SyncToROM.

If the cached profile is absent, Center M may instead treat a larger range as changed and send multiple bounded profile chunks.

This PR must not reproduce or test the write path.

### 3.5 Center M read request

The inspected Center M profile-load path requests the full profile using:

~~~text
ReadProfile
index = 0
~~~

This differs from the Addon's current diagnostic read, which always uses index \`1\`.

The firmware meaning of the request index is still UNKNOWN.

It is also UNKNOWN whether the response byte corresponding to the index simply echoes the request index.

### 3.6 Test button

Center M's vibration Test path uses XInput rather than a direct vendor-HID rumble writer.

The static RE confirms the Test path is not a device-profile readback mechanism.

Do not use Test behavior as proof of profile persistence in this PR.

---

## 4. Existing Addon safety state must remain intact

PR #657 intentionally put vibration firmware mutation into fail-close mode.

Keep this invariant unchanged:

~~~text
all currently supported MSI Claw models
-> MsiClawVibrationFirmwarePolicy.IsDirectMotorProfileAddressVerified(...) == false
-> no production vibration profile write
-> no SyncToROM from normal vibration-strength mutation
-> UI does not expose diagnostic bytes as authoritative percentages
~~~

The user-visible Vibration Strength sliders remain unavailable while the model mapping is unverified.

The physical one-second Left/Right Test path is independent and remains unchanged.

---

## 5. Current diagnostic limitation

Current \`MsiClawVibrationProfileCommand.BuildReadProfile(ushort address)\` builds:

~~~text
0F 00 00 3C 04 01 <addrHi> <addrLo> 01 ...
                  ^^ index 1
                                      ^^ length 1
~~~

Current unverified capture then performs:

~~~text
index 1, address 0x22, length 1
index 1, address 0x23, length 1
~~~

This produced valid-looking responses on CG3EM/MS-1T91, but it cannot answer whether:

- index 0 is the actual profile-read bank used by Center M;
- index 0 and index 1 return the same or different data;
- the response index byte echoes the request index;
- LM/RM should be read together as one contiguous pair;
- the earlier \`50/50\` observation was from the wrong profile index.

The next diagnostic should answer only these questions.

---

## 6. Diagnostic request shape

Add a narrow read-request builder that permits explicit diagnostic profile index and length.

Preferred shape:

~~~csharp
internal static byte[] BuildReadProfile(
    byte profileIndex,
    ushort address,
    byte length)
{
    ...
}
~~~

or an equivalently small diagnostic-specific overload.

For this PR, valid diagnostic inputs are intentionally narrow:

~~~text
profileIndex = 0 or 1
address      = 0x0022
length       = 2
~~~

Do not turn this into a generic arbitrary EEPROM/profile reader.

The generated requests must be exactly:

### Index 0

~~~text
0F 00 00 3C 04 00 00 22 02 ...
~~~

### Index 1

~~~text
0F 00 00 3C 04 01 00 22 02 ...
~~~

No write command is issued.

No \`0x22\` SyncToROM command is issued.

---

## 7. Do not silently change production read semantics

Do not replace all existing read behavior with index 0 merely because Center M uses index 0 for whole-profile reads.

The meaning is not proven yet.

Keep the existing production/helper semantics isolated until hardware evidence resolves the contract.

Acceptable implementation patterns:

### Option A — explicit diagnostic overload

~~~csharp
BuildReadProfile(address)              // preserves current behavior
BuildDiagnosticReadProfile(index, address, length)
~~~

### Option B — explicit parameterized builder with existing wrapper

~~~csharp
BuildReadProfile(address)
    => BuildReadProfile(profileIndex: 1, address, length: 1);

BuildReadProfile(byte profileIndex, ushort address, byte length)
    => ...
~~~

Either is fine.

Do not make an unrelated code cleanup around this command builder.

---

## 8. Replace the unverified diagnostic probe with dual-index pair reads

For an unverified model, Controller-page capture should perform two bounded read-only probes:

~~~text
Probe A:
  RequestIndex = 0
  Address      = 0x0022
  Length       = 2

Probe B:
  RequestIndex = 1
  Address      = 0x0022
  Length       = 2
~~~

Prefer these two pair reads over four separate single-byte reads.

This is both closer to the Center M write shape and lower-noise.

The frontend result must still be:

~~~text
Available    = false
Writable     = false
LeftPercent  = null
RightPercent = null
Status       = FirmwareAddressMappingUnverified
~~~

No raw diagnostic value becomes user-facing state.

---

## 9. Raw response logging is authoritative for this PR

The first purpose of this PR is evidence collection.

Do not make the probe success depend on the current parser's assumption:

~~~text
response[5] == 0x01
~~~

because response-index semantics are precisely what the diagnostic is trying to discover.

For each response, log a bounded response prefix long enough to contain:

~~~text
report ID
command marker
ack
response index byte
address
length
LM
RM
~~~

At least the first 11 bytes should be available when present.

Suggested event:

~~~text
ControllerVibrationProfileIndexProbe
Model=msi.claw.cg3em
ProductId=0x1902
UsagePage=0xFFF0
Usage=0x0040
RequestIndex=0
Address=0x0022
RequestedLength=2
ResponsePrefix=10-00-00-3C-05-00-00-22-02-1E-46
ResponseIndex=0
ResponseLength=2
CandidateLeft=30
CandidateRight=70
StructuralParseSucceeded=True
VerifiedForProduction=False
~~~

and independently for RequestIndex \`1\`.

Use \`Info\` level.

Do not log the entire HID report.

Do not emit this continuously.

It remains tied to explicit Controller-page capture/activation.

---

## 10. Diagnostic structural parser

Add only the smallest parser needed to make the log useful.

For the index-comparison probe, a response may be classified structurally valid when:

~~~text
report length == 64
report[0] == 0x10
report[1] == 0x00
report[2] == 0x00
report[3] == 0x3C
report[4] == 0x05
report[6..7] == requested address
report[8] == requested length
requested length == 2
report[9] <= 100
report[10] <= 100
~~~

Log:

~~~text
ResponseIndex = report[5]
IndexEchoMatched = (report[5] == RequestIndex)
CandidateLeft  = report[9]
CandidateRight = report[10]
~~~

Do not reject the raw evidence solely because \`IndexEchoMatched == false\`.

The response-index behavior is still under investigation.

Do not call \`CandidateLeft/CandidateRight\` authoritative vibration percentages yet.

They are diagnostic candidates only.

---

## 11. Existing strict production parser remains strict

Do not weaken:

\`TryParseReadProfileResponse(...)\`

solely to make the diagnostic accept index 0 responses.

That parser currently encodes historical index-1/single-byte expectations and is still used by the disabled production mutation path.

The new diagnostic parser/helper should be separate or explicitly parameterized.

The PR must not make future production writes easier to enable accidentally.

---

## 12. Hardware test procedure

The target first test is:

~~~text
Device: MSI Claw 8 EX AI+ CG3EM
Board:  MS-1T91
Firmware observed: 0x0419
Physical mode: PID1902
~~~

### Preparation

Before opening the Addon Controller page:

1. Open MSI Center M while the physical controller is PID1902.
2. Set a clearly asymmetric vibration pair, for example:

~~~text
Left  = 30
Right = 70
~~~

3. Allow Center M's normal debounce/save path to settle.
4. Center M may be closed afterward if needed.
5. Do not use SteamAddon to mutate vibration strength.

### Probe

Open SteamAddon Controller page once.

Collect both:

~~~text
RequestIndex=0 / Address=0x22 / Length=2
RequestIndex=1 / Address=0x22 / Length=2
~~~

from the log.

### Interpretation

#### Outcome A

~~~text
index 0 -> 30/70
index 1 -> 50/50
~~~

Interpretation:

> Strong evidence that the Addon's previous diagnostic used the wrong read index for current profile data.

Next PR may then change production read semantics to index 0 and separately validate the Center M write contract.

Do not enable write in this diagnostic PR.

#### Outcome B

~~~text
index 0 -> 30/70
index 1 -> 30/70
~~~

Interpretation:

> Both indices may expose the same underlying current profile data, or request-index semantics may differ from a simple bank selection.

Further write/readback validation is still required.

#### Outcome C

~~~text
index 0 -> 50/50
index 1 -> 50/50
~~~

Interpretation:

> Center M local profile state and this device read region still do not match; the profile command semantics require more RE/hardware evidence.

Do not enable production writes.

#### Outcome D

~~~text
index 0 response shape differs or fails
index 1 remains valid
~~~

Interpretation:

> Whole-profile read semantics cannot be reduced to the current small read command without further protocol analysis.

Keep production fail-closed.

---

## 13. PID1901 is not required for this PR

Do not block this diagnostic PR on a stock-authority PID1901 test.

The newly discovered question is profile-index semantics, and it can be tested on the currently supported PID1902 hardware first.

PID1901 comparison may follow later if index behavior remains ambiguous.

Do not add same-session PID switching to gather evidence.

Do not add a mode-transition diagnostic.

---

## 14. No Center M coexistence architecture

The test observation that Center M can be opened while the physical controller is PID1902 does not change the product authority contract.

This PR must not:

- integrate with Center M;
- depend on Center M IPC;
- keep Center M resident;
- add Center M profile-file parsing to production;
- import \`profile.rec\` as Addon state;
- create a shared profile owner.

Center M is only an external test instrument for setting an asymmetric comparison value.

The standalone Full1902 application remains the product architecture.

---

## 15. Tests

### Command-builder tests

Verify exact bytes for:

~~~text
index 0 / address 0x22 / length 2
index 1 / address 0x22 / length 2
~~~

Expected prefixes:

~~~text
0F-00-00-3C-04-00-00-22-02
0F-00-00-3C-04-01-00-22-02
~~~

Reject unsupported diagnostic indexes/lengths if the implementation exposes a validation helper.

Do not create a general arbitrary-address test matrix.

### Diagnostic parser tests

Cover:

1. structurally valid index-0 response with asymmetric values;
2. structurally valid index-1 response;
3. response index that does not echo request index;
4. wrong address;
5. wrong response length;
6. motor value >100;
7. truncated/invalid report.

For case 3:

~~~text
StructuralParseSucceeded = true
IndexEchoMatched = false
~~~

is acceptable diagnostic evidence.

Do not silently classify it as production-valid.

### Client tests

For an unverified model:

- exactly two pair-read requests are issued;
- request indexes are 0 then 1;
- each request targets 0x22 length 2;
- no \`WriteProfile (0x21)\` is issued;
- no \`SyncToROM (0x22)\` is issued;
- capture still returns unavailable/no authoritative values;
- UI mutation remains unavailable before device write.

### Regression

Existing tests must continue to prove:

- production vibration-strength writes fail closed for all current models;
- physical Left/Right Test path unchanged;
- rumble delivery unchanged;
- lifecycle gate/STOP behavior unchanged;
- Controller LED behavior unchanged;
- Full1902 ownership/HidHide/VIIPER unchanged.

---

## 16. Update RE documentation in the same PR

Update:

\`docs/RE_MSI_ControllerVibration.md\`

Record the latest static RE findings with evidence labels.

Required additions:

### PROVEN

~~~text
Center M UI vibration values are loaded from local profile.rec / ControlProfile,
not from device profile readback.

MotorModule.LeftMotorValue / RightMotorValue are serialized profile offsets
0x22 / 0x23.

Inspected API profile write uses index 1.

Inspected API whole-profile read uses index 0.

Center M can issue a contiguous 2-byte write from offset 0x22 for LM/RM
when those are the changed cached bytes.

Center M queues SyncToROM after the profile write request.
~~~

### UNKNOWN

~~~text
firmware meaning of profile index 0 vs 1

whether read response byte[5] echoes the request index

whether CG3EM firmware 0x0419 maps both requests to the same profile state

whether a successful Center M save request proves persistent device acceptance

whether PID1901/PID1902 share the same firmware profile bank
~~~

Also correct any wording that still treats the Center M UI value as device-read evidence.

Do not delete the historical observations.

---

## 17. No frontend protocol bump

This diagnostic needs no wire-contract change.

Do not bump:

\`FrontendTransportProtocol.CurrentVersion\`

The user-visible snapshot remains unavailable and structurally unchanged.

The new evidence exists only in Runtime logs/tests.

---

## 18. Expected implementation footprint

Keep the PR small.

Likely files:

~~~text
src/SteamInputAddonforClaw/Devices/MSI/Claw/
    MsiClawVibrationProfileCommand.cs
    MsiClawVibrationStrengthClient.cs

tests/SteamInputAddonforClaw.Tests/
    MsiClawVibrationProfileCommandTests.cs
    MsiClawVibrationStrengthClientTests.cs

docs/
    RE_MSI_ControllerVibration.md
~~~

Exact current test filenames may be reused instead of creating duplicates.

No UI XAML changes should be necessary.

No host-composition changes should be necessary.

---

## 19. Explicitly out of scope

Do not:

- enable \`MsiClawVibrationFirmwarePolicy\` for CG3EM;
- enable any production vibration-strength write;
- change UI fallback to 50 or 100;
- parse Center M \`profile.rec\` in production;
- add Center M IPC integration;
- change runtime rumble scaling;
- introduce a software gain multiplier;
- change the one-second Test pulse;
- write index 0;
- write index 1;
- issue diagnostic SyncToROM;
- switch PID for the test;
- add periodic polling;
- add a VibrationManager/profile manager;
- add a generic firmware-profile browser;
- add new authority/state/epoch/lock machinery;
- modify LED code;
- modify HidHide/VIIPER/routing.

---

## 20. Acceptance criteria

This PR is complete when:

~~~text
Controller page activation on an unverified model
-> exact command HID is resolved
-> ReadProfile index 0, offset 0x22, length 2
-> bounded raw response logged
-> ReadProfile index 1, offset 0x22, length 2
-> bounded raw response logged
-> no firmware mutation
-> frontend remains unavailable
~~~

The logs must make it possible to answer:

~~~text
What did index 0 return?
What did index 1 return?
What response index byte was observed?
Did the response address/length match?
What candidate LM/RM bytes were present?
~~~

And the production invariant remains:

> No MSI Claw model receives production vibration-profile writes until the exact read/write profile semantics are hardware-validated.
