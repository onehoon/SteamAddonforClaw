# Work Order — Full1902 Vibration Firmware Address Validation Hotfix

> Date: 2026-10-04  
> Repository baseline reviewed: main at 851ed8c8f2d2141bd0c1001c5a019489c93622b7  
> Scope: one small production-safety PR  
> Affected feature: Controller > Vibration Strength  
> Product model: standalone Full1902 application

## 1. Goal

Correct the production assumption introduced by PR #651 that treats MSI motor-profile serialization offsets 0x22 / 0x23 as proven direct firmware/EEPROM addresses on every currently supported MSI Claw model.

Do not fix this by hard-coding 50%, 100%, or any other default.

The immediate hotfix must:

1. stop production vibration-strength writes on models where direct-address ownership has not been physically proven;
2. keep the existing firmware read path available only as bounded diagnostic evidence;
3. log enough raw evidence to compare PID1901 and PID1902 behavior on real hardware;
4. keep the existing physical rumble/Test implementation and Full1902 ownership lifecycle unchanged;
5. update the vibration RE document so the evidence boundary is explicit;
6. permit a later one-line/small capability enablement after a model is physically validated.

This is a fail-close correction, not a vibration architecture rewrite.

## 2. Required Full1902 authority documents

Read these together before implementation:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

Current controller authority remains:

~~~text
Center M Enabled
-> MSI / stock authority
-> desired physical PID1901

Center M Disabled
-> Addon Runtime authority
-> desired physical PID1902
-> Addon-owned DirectInput / HidHide / VIIPER presentation
~~~

This hotfix must not add another controller authority or alter those lifecycle rules.

## 3. Evidence correction

The current production code and the original vibration work order went beyond what the strongest MSI RE currently proves.

### Proven from current MSI Center M RE

The MSI profile serialization contains:

~~~text
MotorModule.LeftMotorValue  -> serialized offset 34 decimal = 0x22
MotorModule.RightMotorValue -> serialized offset 35 decimal = 0x23
~~~

The current MSI RE explicitly states:

~~~text
profile offset 0x22/0x23 -> direct EEPROM address
NOT PROVEN
~~~

Sources reviewed:

- docs/RE_MSI_ControllerVibration.md
- supplied MSI_COMPLETE_RESEARCH_RESULT.md, especially the MotorModule/profile-offset findings
- current PR #651 implementation
- current Full1902 authority documents

The RE material contains older on-device round-trip evidence for these values, but the repository does not currently bind that direct-address proof to each supported model/firmware contract:

~~~text
msi.claw.a2vm.7   / MS-1T42
msi.claw.a2vm.8   / MS-1T52
msi.claw.cg3em    / MS-1T91
~~~

Therefore the current production implementation must not infer:

~~~text
known MSI Claw model
-> TDP supported
-> direct vibration profile address is proven
~~~

Those are unrelated capabilities.

## 4. Real field evidence from 2026-10-04

The tested machine is:

~~~text
HardwareDeviceModel = msi.claw.cg3em
BaseBoard = MS-1T91
~~~

The user observed MSI Center M's motor values/default presentation as:

~~~text
Left  = 50%
Right = 50%
~~~

But immediately after PR #651 the Addon Controller page displayed:

~~~text
Left  = 100%
Right = 100%
~~~

The Addon log session in Addon/Log/1004 shows the first production vibration mutations as:

~~~text
ControllerVibrationMutationStarted LeftPercent=50 RightPercent=100
ControllerVibrationMutationSucceeded LeftPercent=50 RightPercent=100

ControllerVibrationMutationStarted LeftPercent=50 RightPercent=50
ControllerVibrationMutationSucceeded LeftPercent=50 RightPercent=50
~~~

There is no earlier vibration mutation in that session that writes 100/100.

The current UI also contains no Value=100 vibration default and the frontend unavailable snapshot contains no fabricated 100 value.

Therefore the practical finding is:

> The Addon did not invent a UI default of 100. The current 0x22/0x23 read path produced values that were presented as 100/100, while Center M's user-facing values were known as 50/50.

That mismatch invalidates the production assumption that the current direct-address read is automatically the same setting as the Center M motor-strength UI on CG3EM.

Do not paper over this evidence by changing the UI default.

## 5. Current code defect

Current composition in AddonProcessHost effectively uses the TDP capability as the admission check for the vibration firmware client:

~~~csharp
if (!_headlessUninstallPreparation
    && startupResult.HardwareDeviceModel is { } tdpModel
    && MsiClawTdpPolicy.TryResolve(tdpModel, out _))
{
    ...
    controllerVibrationStrengthClient = new(
        GetMsiControllerDevices(),
        new MsiClawControlHidResolver(),
        new WindowsMsiClawVibrationProfileIo());
}
~~~

This is not a valid capability relationship.

MsiClawTdpPolicy.TryResolve(model) proves only that the model has a supported TDP policy.

It does not prove that:

~~~text
0x0022 = persistent Left vibration strength address
0x0023 = persistent Right vibration strength address
~~~

for that model/firmware.

The second problem is at the hardware mutation boundary.

MsiClawVibrationStrengthClient.SetAsync() currently permits:

~~~text
read 0x22 / 0x23
-> WriteProfile 0x22/0x23
-> SyncToROM
-> readback
~~~

as long as Center M is exactly Disabled.

Strong identity and Center M ownership checks are necessary, but they do not prove the semantic meaning of the firmware addresses.

## 6. Hotfix policy

Introduce one narrow model-specific vibration firmware capability decision.

Do not create a generic capability framework or manager.

A small static policy is sufficient, for example:

~~~csharp
internal static class MsiClawVibrationFirmwarePolicy
{
    internal static bool IsDirectMotorProfileAddressVerified(HandheldDeviceModelId modelId)
        => modelId switch
        {
            // Add a model here only after real hardware evidence proves
            // 0x0022/0x0023 are the Center M Left/Right motor settings
            // for that model/firmware contract.
            _ => false,
        };
}
~~~

Exact naming may follow current conventions.

### Initial table

At the time of this hotfix, do not mark any current model verified merely because it is supported by the Addon.

Specifically, no automatic true for:

~~~text
MS-1T42
MS-1T52
MS-1T91
~~~

until model-specific evidence is recorded.

This deliberately makes the production firmware strength editor fail closed until validation.

A later evidence-backed PR may enable a specific model without redesigning the feature.

## 7. Separate vibration capability from TDP composition

Refactor the current AddonProcessHost composition only as much as required to remove the false TDP dependency.

Keep TDP composition exactly under MsiClawTdpPolicy.

Vibration composition should use the actual detected MSI Claw model directly.

Conceptually:

~~~csharp
var vibrationModel = startupResult.HardwareDeviceModel;

MsiClawVibrationStrengthClient? controllerVibrationStrengthClient = null;

if (!_headlessUninstallPreparation && vibrationModel is { } model)
{
    controllerVibrationStrengthClient = new(
        model,
        GetMsiControllerDevices(),
        new MsiClawControlHidResolver(),
        new WindowsMsiClawVibrationProfileIo());
}
~~~

The client may still exist on an unverified model so that an explicit read-only diagnostic capture can collect evidence.

Production availability/write permission must be controlled by the vibration-specific firmware policy, not by TDP support.

Do not create a second device-model resolver.

Reuse startupResult.HardwareDeviceModel.

## 8. Production capture semantics on an unverified model

Opening Controller > Vibration Strength must not present raw 0x22/0x23 bytes as authoritative percentages when the model's direct-address mapping is unverified.

For an unverified model:

~~~text
CaptureControllerVibrationStrengthAsync
-> perform bounded diagnostic read of current 0x22/0x23 only if safe
-> log raw evidence
-> return FrontendControllerVibrationStrengthSnapshot.Available = false
-> Writable = false
-> LeftPercent = null
-> RightPercent = null
-> status = model/address mapping not verified
~~~

The UI then naturally shows:

~~~text
Left  —
Right —
sliders disabled
~~~

Do not display 50% or 100% as a fallback.

Do not preserve a previous draft as though it were hardware truth.

Suggested user-facing status:

~~~text
Vibration firmware mapping is not verified for this MSI Claw model.
~~~

Keep wording concise.

## 9. Production mutation must fail before any firmware write

MsiClawVibrationStrengthClient.SetAsync() must check model capability before performing a production mutation.

Required ordering:

~~~text
validate percent bounds
-> check model-specific direct-address capability
-> if unverified: return Unavailable immediately
-> only then enter the existing strongly-identified transaction path
~~~

For an unverified model:

~~~text
WriteProfile count = 0
SyncToROM count = 0
EEPROM/profile writes = 0
~~~

This guard belongs at the real hardware mutation boundary even if the UI is already disabled.

Do not rely only on frontend visibility or button state.

Recommended reason:

~~~text
FirmwareAddressMappingUnverified
~~~

or an equivalently precise reason consistent with the current client.

## 10. Read-only diagnostic evidence

The hotfix must make the existing capture path useful for hardware validation without turning diagnostics into another authority.

For each attempted diagnostic read, log enough information to distinguish:

- model;
- physical mode/PID;
- usage page / usage;
- address;
- response prefix;
- parsed byte;
- parse success/failure.

Example event:

~~~text
[ControllerVibration]
ControllerVibrationProfileProbe
Model=msi.claw.cg3em
ProductId=0x1902
UsagePage=0xFFF0
Usage=0x0040
Address=0x0022
ResponsePrefix=10-00-00-3C-05-01-00-22-01-64
ParsedValue=100
VerifiedForProduction=False
~~~

And independently for 0x0023.

### Logging level

Use Info for this temporary bounded probe event so it appears in normal Addon logs used for hardware validation.

This is low frequency because it occurs only on explicit vibration capture/page activation.

Do not log the complete HID report at Info.

A short response prefix covering the validated header/address/length/value fields is enough.

Do not log on every slider movement.

## 11. Preserve parser safety

Keep the existing parser requirements:

- response report id;
- command marker;
- read-profile ack;
- requested address;
- length;
- value bounds.

The diagnostic log must not weaken parsing.

It may log a bounded prefix from a rejected response for evidence, but the frontend must still treat it as unavailable.

Do not accept a response merely because byte 9 happens to be in 0..100.

## 12. Update docs/RE_MSI_ControllerVibration.md

Correct the RE document in the same PR.

The document must clearly separate three evidence levels.

### A. MSI profile serialization — proven

~~~text
LeftMotorValue  serialized offset 0x22
RightMotorValue serialized offset 0x23
~~~

### B. Historical/on-device direct profile read/write evidence — exists

Document that older on-device RE reported round-trip behavior, but do not generalize it to every current MSI Claw model without model identity evidence.

### C. Current production model mapping — model-specific validation required

State explicitly:

~~~text
A serialization offset is not, by itself, proof that the same numeric value is
the direct persistent firmware address on MS-1T42, MS-1T52, or MS-1T91.
~~~

For CG3EM/MS-1T91, record the 2026-10-04 mismatch:

~~~text
Center M UI observed 50/50
Addon 0x22/0x23 read path presented 100/100 before Addon mutation
~~~

and classify the direct mapping as unverified pending the probe procedure below.

Do not rewrite historical evidence as though it never existed.

## 13. Precedence over the original vibration work order

This hotfix supersedes the direct-address assumptions in:

docs/work-order/FULL1902_CONTROLLER_VIBRATION_STRENGTH_WORK_ORDER_2026-10-03.md

where they treat 0x0022/0x0023 as a production-safe direct address for all supported MSI Claw hardware.

The original UI decisions remain valid:

- Controller-page SettingsExpander;
- icon;
- collapsed by default;
- independent Left/Right controls;
- 500 ms debounce;
- no software gain;
- physical Test button architecture.

The correction is only the firmware evidence/capability boundary.

Do not remove the historical work order.

## 14. Do not automatically restore 50/50

The current test machine has already received Addon writes to 0x22/0x23.

Do not add startup code that automatically writes:

~~~text
50 / 50
~~~

to "repair" that machine.

The fact that Center M presents 50/50 as the user's known default does not authorize the Addon to guess that every machine should be restored to 50/50.

The hotfix itself performs no automatic firmware correction.

Any restoration test must be explicit and hardware-validated.

## 15. Hardware validation procedure — CG3EM / MS-1T91

Use the new read-only probe before re-enabling production writes.

### Phase A — stock / PID1901

Boot with Center M Enabled / MSI authority.

Record:

~~~text
Model
PID
UsagePage/Usage
0x22 response prefix + parsed byte
0x23 response prefix + parsed byte
Center M Left/Right UI values
~~~

Start with the known 50/50 state if available.

Then, if safe, change one side in Center M to an unmistakable asymmetric pair, for example:

~~~text
Left  = 40
Right = 70
~~~

Close/settle the Center M UI and capture again.

Required evidence for a direct mapping candidate:

~~~text
Center M 40/70
-> diagnostic read 40/70
~~~

A single constant 50/50 match is not sufficient proof.

### Phase B — Addon / PID1902

Use the normal reboot-bound Disable Center M and Restart path.

Do not perform a same-session authority handoff for this test.

After Full1902 PID1902 ownership is established, open Controller > Vibration Strength and collect the same read-only probe.

Compare with the last stock values.

Possible outcomes:

~~~text
PID1901 tracks Center M exactly
PID1902 tracks same values exactly
-> strong evidence that the direct profile addresses survive the mode transition

PID1901 tracks Center M
PID1902 becomes 100/100 or another pair
-> mode-dependent profile/firmware behavior; do not enable production write yet

PID1901 does not track Center M
-> 0x22/0x23 are not the Center M motor setting on this model, or another translation layer exists

read/response shape differs
-> protocol/model-specific investigation required
~~~

### Phase C — only after read mapping is proven

A separate evidence-backed change may enable IsDirectMotorProfileAddressVerified(msi.claw.cg3em).

Then perform the existing bounded write/readback acceptance test.

Do not combine "we saw a plausible byte" with production enablement in the hotfix itself.

## 16. Tests

### Capability policy tests

Add exact tests proving:

~~~text
msi.claw.a2vm.7 -> unverified
msi.claw.a2vm.8 -> unverified
msi.claw.cg3em  -> unverified
unknown model   -> unverified
~~~

until evidence changes the table.

When a future model is enabled, that PR must update the focused test and cite its hardware evidence.

### Client mutation tests

For an unverified model:

- valid 50/50 mutation returns Unavailable;
- WriteAsync is never called;
- BuildSyncToRom is never emitted;
- no readback is presented as a successful production mutation;
- percent bounds are still validated normally.

### Diagnostic capture tests

For an unverified model:

- valid raw 0x22/0x23 responses may be parsed for diagnostics;
- frontend production snapshot remains unavailable;
- no writes occur;
- the diagnostic event includes model, PID, endpoint, address, parsed value, and bounded response prefix.

### Frontend/UI tests

Verify:

- unavailable/unverified snapshot does not show 50 or 100;
- both sliders are disabled/hidden according to the existing unavailable rendering;
- no debounce/mutation is scheduled from rendering;
- no firmware write can be triggered through UI on unverified hardware.

### Regression tests

Existing tests must continue to prove:

- physical rumble delivery unchanged;
- Left/Right Test bounded pulse unchanged;
- one-second Test still releases lifecycle gate before its delay;
- suspend/hibernate/teardown STOP behavior unchanged;
- PID1902 ownership/HidHide/VIIPER paths unchanged;
- Controller LED behavior unchanged;
- TDP support unchanged.

Do not create scheduler-interleaving tests unrelated to this real hardware mismatch.

## 17. No frontend protocol bump unless contract shape changes

This hotfix can be implemented without adding new public frontend fields.

Prefer reusing:

~~~text
Available
Writable
TestAvailable
LeftPercent
RightPercent
Status
~~~

with unverified hardware represented as unavailable.

If no wire contract shape/enum changes, do not bump FrontendTransportProtocol.CurrentVersion.

Do not bump the protocol merely because internal semantics or status text changed.

If implementation genuinely adds a new wire field/enum, then bump the protocol in the normal way.

## 18. Expected code changes

Likely modifications:

~~~text
src/SteamInputAddonforClaw/Devices/MSI/Claw/
    MsiClawVibrationStrengthClient.cs
    [small vibration firmware policy file, only if clearer than a local static helper]

src/SteamInputAddonforClaw/Hosting/
    AddonProcessHost.cs

src/SteamInputAddonforClaw/Frontend/
    InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.UI/Views/
    ControllerPage.xaml.cs   // only if needed for clearer unverified-state presentation

docs/
    RE_MSI_ControllerVibration.md

tests/SteamInputAddonforClaw.Tests/
    focused vibration policy/client/frontend tests

tests/SteamInputAddonforClaw.UiTests/
    only the focused unavailable-state assertion if current coverage needs it
~~~

Do not move TDP code or refactor unrelated device capability infrastructure.

## 19. Explicitly out of scope

Do not add:

- hard-coded default 50%;
- hard-coded default 100%;
- software vibration gain;
- CTW-style "firmware 100 + software multiplier" policy;
- automatic firmware restore;
- periodic firmware polling/reassertion;
- a generic capability registry;
- a VibrationManager;
- a controller-settings manager;
- a new EEPROM abstraction layer;
- a new controller authority;
- same-session Center M/Addon authority switching;
- changes to HidHide ownership;
- changes to VIIPER ownership;
- changes to PID1901/PID1902 restoration;
- changes to physical rumble packet format;
- changes to the one-second Test pulse unless a separate real defect is found.

## 20. Acceptance criteria

The hotfix is complete when all of the following are true:

~~~text
Known MSI Claw model alone
!= proof of vibration firmware direct-address support
~~~

~~~text
TDP support
!= vibration firmware support
~~~

~~~text
unverified model
-> Controller vibration strength is unavailable
-> no 0x22/0x23 production write
-> no SyncToROM
-> no fabricated 50/100 UI value
~~~

~~~text
explicit page/capture probe
-> bounded read only
-> logs model + PID + endpoint + address + response prefix + parsed byte
-> enables PID1901/PID1902 comparison
~~~

~~~text
existing Full1902 physical rumble/Test/lifecycle
-> unchanged
~~~

And most importantly:

> A profile serialization offset must not become a production firmware write contract until that exact model's hardware evidence proves the mapping.
