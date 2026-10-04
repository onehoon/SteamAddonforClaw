# Work Order — Full1902 Controller Vibration Production Persistence, Lifecycle Reapply, and Diagnostic Cleanup

> Date: 2026-10-04
> Repository baseline reviewed: main at 26c17beeccf3dc4267700a37085fc2cdd16c981e
> Scope: one focused production-conversion PR
> Initial production target: MSI Claw 8 AI+ EX / msi.claw.cg3em / MS-1T91
> Hardware acceptance firmware: 0x0419
> Product architecture: standalone Full1902
> Default vibration strength: Left 50 / Right 50

## 1. Goal

Convert Controller > Vibration Strength from the temporary firmware-readback/fail-closed implementation into the final production model proven by the CG3EM hardware test.

The final product contract is:

~~~text
Addon persisted settings
Left  = 0..100
Right = 0..100
        |
        | single desired-state authority
        v
owned PID1902 control HID
        |
        v
WriteProfile
index  = 1
offset = 0x0022
length = 2
payload = Left, Right

NO SyncToROM
~~~

The Addon settings, not device readback, are the persistent source of truth.

The existing Controller-page Vibration Strength card remains the user-facing UI.

Do not move vibration settings to the Settings page.

The production implementation must:

1. persist one global Left/Right pair in the existing settings.json;
2. default to 50 / 50;
3. immediately apply a user edit when a healthy owned PID1902 session exists;
4. reapply the persisted pair after initial Full1902 ownership;
5. reapply after successful real physical-controller recovery;
6. reapply once after Sleep/Hibernate Resume;
7. perform zero writes for normal Xbox360 <-> SteamDeck presentation changes;
8. use one contiguous pair write and never SyncToROM;
9. remove obsolete production single-byte/readback/ROM-commit code;
10. keep the Developer Vibration Test diagnostics for future A2VM hardware investigation.

Do not add a new vibration manager, reconcile loop, watchdog, epoch, barrier, or lifecycle state machine.

---

## 2. Required architecture documents

Read and preserve:

- docs/Full 1902 Implementation/README.md
- docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
- docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
- docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
- docs/RE_MSI_ControllerVibration.md

Current authority remains:

~~~text
Center M Enabled
-> MSI / stock controller authority
-> desired PID1901
-> Addon must not mutate controller vibration profile

Center M Disabled
-> Addon Runtime controller authority
-> desired PID1902
-> DirectInput + HidHide + VIIPER owned by Addon
-> Addon may apply the persisted vibration pair to the exact owned PID1902 control HID
~~~

Steam/BPM state only selects virtual presentation.

It must not control vibration-strength application.

---

## 3. Evidence that changes the production design

### 3.1 Center M behavior

Static Center M RE proved:

~~~text
MotorModule.LeftMotorValue  -> serialized profile-relative offset 0x22
MotorModule.RightMotorValue -> serialized profile-relative offset 0x23

profile write index = 1
contiguous LM/RM write can use:
offset = 0x22
length = 2
~~~

Center M UI values are loaded from its local profile state rather than authoritative device readback.

On the target system, changing Center M vibration away from 50/50, fully exiting Center M from the tray, and relaunching Center M returned the UI to 50/50.

Therefore Center M's UI state is not evidence of a durable hardware/EEPROM source of truth.

### 3.2 PR #662 runtime write hardware acceptance

The CG3EM developer probe sent:

~~~text
0F 00 00 3C 21 01 00 22 02 00 64 ...
~~~

meaning:

~~~text
Left = 0
Right = 100
SyncToROM = false
~~~

Runtime log evidence showed:

~~~text
ControllerVibrationProfileWriteProbeCompleted
Left=0
Right=100
TransportSucceeded=True
SyncToRom=False
~~~

The subsequent device read returned:

~~~text
CandidateLeft=0
CandidateRight=100
~~~

for both diagnostic request-index variants.

The existing physical Left/Right Test path then emitted independent full-scale motor requests with mandatory STOP.

The field test reported that the hardware behavior changed as expected.

Restore then sent:

~~~text
Left=50
Right=50
TransportSucceeded=True
SyncToRom=False
~~~

and subsequent readback returned 50/50.

### 3.3 Production conclusion

For the tested CG3EM contract:

~~~text
index 1 / relative offset 0x22 / length 2 / Left+Right
~~~

has a real live physical effect without SyncToROM.

Therefore production no longer needs to model these values as persistent firmware/EEPROM settings.

The Addon should persist its own desired pair and reapply it to the runtime controller state.

---

## 4. Default and persistence contract

Add a small typed persisted contract, preferably under:

~~~text
src/SteamInputAddonforClaw.Contracts/ControllerVibration/
~~~

Suggested shape:

~~~csharp
public sealed record ControllerVibrationSettings(
    int LeftPercent,
    int RightPercent)
{
    public static ControllerVibrationSettings Default { get; } = new(50, 50);
}
~~~

Validation:

~~~text
LeftPercent  = 0..100
RightPercent = 0..100
~~~

The default is deliberately:

~~~text
50 / 50
~~~

because:

- Center M presents 50/50 as its normal baseline on the target device;
- repeated CG3EM profile reads also observed 50/50;
- the user explicitly selected 50/50 as the Addon default.

Do not add:

- Enabled/Disabled;
- per-game vibration settings;
- separate AC/DC values;
- motor curves;
- software attenuation;
- firmware-persistence flags.

This is one global two-value setting.

---

## 5. AppSettings is the persistent authority

Add:

~~~csharp
public ControllerVibrationSettings ControllerVibration { get; init; }
    = ControllerVibrationSettings.Default;
~~~

to the existing AppSettings.

Use the existing SettingsStore only.

Do not create another file, registry key, profile database, or controller-side persistence mechanism.

### 5.1 Load behavior

A settings file created before this feature must load:

~~~text
ControllerVibration = 50 / 50
~~~

when the property is absent.

Malformed vibration data must fall back only this feature to 50/50.

Do not reset unrelated settings because one vibration field is malformed.

Use the same isolated parser pattern already used for Controller LED.

### 5.2 Save behavior

Include ControllerVibration in the existing settings payload.

StartupSettingsCoordinator should expose:

~~~csharp
public ControllerVibrationSettings ControllerVibration => Settings.ControllerVibration;
~~~

and one validated whole-record mutation, conceptually:

~~~csharp
public bool ChangeControllerVibrationSettings(ControllerVibrationSettings settings)
~~~

Use save-then-current ordering.

No second in-memory authority is allowed.

---

## 6. Production hardware policy

The old policy:

~~~text
MsiClawVibrationFirmwarePolicy
IsDirectMotorProfileAddressVerified(...)
~~~

describes the obsolete direct-address theory.

The validated contract is a Center M-compatible serialized profile pair write.

Replace the policy with a narrow production policy such as:

~~~csharp
internal static class MsiClawVibrationProfilePolicy
{
    internal static bool IsProductionPairWriteVerified(HandheldDeviceModelId modelId)
        => modelId.Value == "msi.claw.cg3em";
}
~~~

Do not mark either A2VM model as production-capable in this PR.

Current production table:

| Addon model | Board | Production vibration pair write |
| --- | --- | --- |
| msi.claw.cg3em | MS-1T91 | Verified |
| msi.claw.a2vm.7 | MS-1T42 | Not yet hardware-tested |
| msi.claw.a2vm.8 | MS-1T52 | Not yet hardware-tested |

A2VM can later be enabled by a small evidence-backed policy change after using the preserved Developer diagnostics on real hardware.

Do not create a generic future-model capability database.

---

## 7. Production command becomes one pair write

Promote the already hardware-tested pair builder from diagnostic-only naming to the production command.

Preferred shape:

~~~csharp
internal static byte[] BuildMotorPairWrite(int leftPercent, int rightPercent)
~~~

Exact 64-byte layout:

~~~text
[0]    0x0F
[1]    0x00
[2]    0x00
[3]    0x3C
[4]    0x21
[5]    0x01
[6]    0x00
[7]    0x22
[8]    0x02
[9]    Left
[10]   Right
~~~

Example default:

~~~text
0F-00-00-3C-21-01-00-22-02-32-32
~~~

Example full asymmetry:

~~~text
0F-00-00-3C-21-01-00-22-02-00-64
~~~

There is exactly one production write per apply attempt.

---

## 8. Remove obsolete production command paths

The final production implementation no longer needs the old single-channel + ROM-commit model.

Remove production use of, and remove the helpers entirely if no Developer diagnostic still uses them:

~~~text
BuildWriteProfile(address, percent)
BuildSyncToRom()
CommitChannelAsync(...)
FailedAfterReadbackAsync(...)
single-byte Left write
single-byte Right write
SyncToROM after each channel
~~~

Also remove the old production sequence:

~~~text
read current Left
write Left
SyncToROM
read Left
read current Right
write Right
SyncToROM
read Right
~~~

No production path should issue command 0x22 / SyncToROM.

No production path should write 0x22 and 0x23 separately.

Do not keep dead compatibility wrappers for this unreleased implementation.

---

## 9. Device readback is no longer the production source of truth

The normal Controller page must not read the device to discover the user's saved vibration setting.

The product source of truth is:

~~~text
StartupSettingsCoordinator.Settings.ControllerVibration
~~~

The existing frontend capture may remain as a UI/capability projection if that is the lowest-churn implementation, but it must become:

~~~text
persisted Left/Right pair
+ current production capability
+ current physical Test availability
~~~

and must perform zero HID profile reads.

For example:

~~~text
CaptureControllerVibrationStrengthAsync()
-> read current persisted ControllerVibrationSettings
-> derive Available/Writable from supported model + Addon authority
-> derive TestAvailable from current physical rumble path
-> return persisted Left/Right
-> zero profile HID read
~~~

This lets the existing Controller-page structure remain stable without treating device readback as settings authority.

Update comments/status strings accordingly.

Do not say:

~~~text
Firmware values read successfully.
persistent firmware ceiling
firmware values are authoritative
~~~

Use wording such as:

~~~text
Set the vibration strength applied while the Addon owns the controller.
~~~

---

## 10. Production apply owner

Refactor MsiClawVibrationStrengthClient so the production apply operation is a simple exact-device write.

Preferred production shape:

~~~csharp
Task<bool> ApplyAsync(
    ControllerVibrationSettings settings,
    MsiClawPhysicalIdentity expectedIdentity,
    CancellationToken cancellationToken)
~~~

Requirements:

1. settings validate 0..100;
2. model passes MsiClawVibrationProfilePolicy;
3. expected identity is Strong;
4. resolve exactly one owned PID1902 control HID;
5. require VID 0x0DB0;
6. require PID 0x1902;
7. require UsagePage 0xFFF0 / Usage 0x0040;
8. require resolved physical identity to strongly match the current owned identity;
9. emit exactly one BuildMotorPairWrite(left,right);
10. return transport success/failure;
11. no readback;
12. no SyncToROM.

Use the existing transaction gate so production apply and Developer diagnostics cannot write the profile simultaneously.

Do not add another lock.

---

## 11. Host-side authority gate

Add a host helper parallel to Controller LED:

~~~csharp
ApplyOwnedControllerVibrationSettingsAsync(
    ControllerVibrationSettings settings,
    CancellationToken cancellationToken)
~~~

It must fail closed unless:

~~~text
Center M startup authority == Disabled
AND
_physicalOwnership.LiveInputSource.IsRunning == true
AND
_physicalOwnership.OwnedPhysicalIdentity is current/Strong
AND
current model is production pair-write verified
~~~

Then call the vibration client with that exact owned identity.

On apply failure:

~~~text
do not change PID
do not change HidHide
do not detach VIIPER
do not roll persisted vibration settings back
log feature-local failure
keep controller usable
~~~

This helper is a feature apply seam, not another controller authority.

---

## 12. User edit flow

Keep the existing Controller-page Vibration Strength card and the current approximately 500 ms debounce behavior.

Do not move this UI.

User flow:

~~~text
Controller
-> Vibration Strength
-> Left / Right sliders
-> debounce latest pair
-> persist whole pair
-> best-effort immediate apply to current owned PID1902
~~~

The persisted pair is authoritative before hardware apply.

If immediate hardware application fails:

~~~text
saved value remains
-> next startup/recovery/resume retries the saved value
~~~

Do not restore the previous saved value merely because one HID write failed.

The existing Left Test and Right Test buttons remain independent physical-rumble tests.

Do not change their one-second pulse or mandatory STOP behavior.

---

## 13. Initial startup application

Current Full1902 startup already establishes:

~~~text
owner.AcquireAsync()
-> PID1902 ownership proven
-> DirectInput first valid state
-> HidHide exact targets proven
-> LiveInputSource healthy
-> Controller LED apply
-> presentation attach
~~~

Apply the persisted vibration pair at the same physical-settings stage.

Required ordering:

~~~text
physical ownership acquired
-> live DirectInput source proven healthy
-> apply persisted LED
-> apply persisted vibration pair
-> continue Win+G / presentation startup
~~~

The exact LED/vibration ordering is not safety-significant; keep it deterministic and close together.

A vibration apply failure is feature-local and must not prevent controller presentation startup.

Do not apply vibration before ownership is committed.

Do not apply immediately after only the PID1901 -> PID1902 mode command.

---

## 14. Real physical recovery application

Reuse the existing recovery seam:

~~~text
RequestOwnedControllerRecovery(...)
-> RecoverOwnedControllerPhysicalInputAsync(...)
-> physical.RecoverLostInputAsync(...)
~~~

Current LED behavior already does:

~~~text
if real recovery succeeded
-> reapply persisted LED
-> RequestControllerPresentationReconcile("PhysicalInputRecovered")
~~~

Extend the same success boundary:

~~~text
if result.IsOwned
AND result.Reason != "RecoveryNotNeeded"
-> reapply persisted LED
-> reapply persisted vibration pair
-> presentation reconcile
~~~

This covers real supported lifecycle events including:

- unexpected DirectInput session loss;
- physical device loss and PnP return;
- PID1901 drift and PID1902 reclaim;
- control HID re-enumeration accompanying real recovery.

Do not reapply on:

~~~text
RecoveryNotNeeded
unrelated Device Arrival while source is healthy
failed/ambiguous recovery
~~~

Do not create an independent vibration PnP watcher.

---

## 15. Sleep / Hibernate / Resume

Resume requires one vibration reapply even if DirectInput survived.

A physical controller can survive resume with the old process-owned DirectInput source still healthy while volatile controller-side feature state has reset.

LED already handles this real lifecycle.

Do the same for vibration.

Current host path:

~~~text
OnPowerResumeObserved()
-> RequestControllerPresentationReconcile("PowerResume")
-> delayed LED reapply
~~~

Prefer consolidating the existing LED-only delayed helper into one narrow physical-controller-settings helper rather than scheduling separate feature timers.

Conceptual shape:

~~~csharp
private async Task ReapplyOwnedControllerHardwareSettingsAfterResumeAsync(
    CancellationToken cancellationToken)
{
    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

    if (shutdown) return;

    await ApplyOwnedControllerLedSettingsAsync(...);
    await ApplyOwnedControllerVibrationSettingsAsync(...);
}
~~~

This is a host-local convenience helper only.

Do not create a ControllerHardwareSettingsManager, generic feature registry, or resume pipeline abstraction.

One existing 500 ms bounded settle is sufficient.

### Duplicate recovery + resume apply

A resume can also trigger a real physical recovery.

In that case the same desired pair may be written once from the recovery-success path and once from the delayed resume path.

That is acceptable.

Do not add an epoch/dedup token solely to suppress this harmless duplicate pair write.

Only add more synchronization if real hardware evidence demonstrates a problem.

---

## 16. Events that must perform zero vibration-strength writes

Do not apply the persisted pair for:

~~~text
Xbox360 -> SteamDeck
SteamDeck -> Xbox360
Steam game start/end
Big Picture enter/exit
normal presentation reconcile
Overlay show/hide
frontend open/close
QAM open/close
ordinary status refresh
unrelated Device Arrival with healthy source
~~~

Vibration strength belongs to physical-controller ownership, not presentation ownership.

ReconcileControllerPresentationAsync must stay free of vibration-strength writes.

---

## 17. Frontend production semantics

The current frontend methods can retain their names to minimize churn:

~~~text
CaptureControllerVibrationStrengthAsync
SetControllerVibrationStrengthAsync
TestControllerVibrationMotorAsync
~~~

but their production meaning changes.

### Capture

~~~text
no hardware profile read
-> return persisted desired Left/Right
-> return Writable only when current model is production-verified and Center M authority is Disabled
-> TestAvailable remains independent
~~~

### Set

Required order:

~~~text
validate
-> verify production feature availability
-> persist whole ControllerVibrationSettings pair
-> best-effort apply through host callback
-> return snapshot containing persisted desired pair
~~~

If persistence succeeds but hardware apply fails:

- keep the new desired pair;
- return it in the snapshot;
- failure/status text should say that the setting was saved but could not be applied now and will be retried on the normal controller lifecycle;
- never roll settings back.

Do not let InProcessAddonFrontendControl enumerate/open HID directly.

Pass it one host callback parallel to applyControllerLedSettings.

---

## 18. Controller UI cleanup

Keep:

~~~text
Controller
-> Vibration Strength
   -> Left Motor slider + percent + Test
   -> Right Motor slider + percent + Test
~~~

Default display:

~~~text
50%
50%
~~~

when no previous setting exists.

Change the expander description from:

~~~text
Set the persistent firmware ceiling for each controller motor.
~~~

to wording that matches the actual architecture, for example:

~~~text
Set the vibration strength applied while the Addon owns the controller.
~~~

The sliders remain 0..100 with step 1.

Keep the current debounce.

Do not add another settings page, Enabled toggle, per-game values, or software multiplier controls.

---

## 19. Production code cleanup

Remove obsolete code that existed only for the earlier direct-firmware/readback theory.

Target cleanup includes, where no Developer diagnostic still needs the exact helper:

~~~text
MsiClawVibrationFirmwarePolicy
IsDirectMotorProfileAddressVerified
BuildWriteProfile(single byte)
BuildSyncToRom
CommitChannelAsync
FailedAfterReadbackAsync
production TryReadPairAsync
production TryReadValueAsync
production initial/final readback verification
production FirmwareAddressMappingUnverified status model
production persistent firmware ceiling wording
~~~

Replace the policy with the small pair-write production policy from section 6.

### Keep only if used by Developer diagnostics

~~~text
BuildDiagnosticReadProfile
TryParseDiagnosticReadProfileResponse
diagnostic raw response logging
RunDiagnosticMotorPairWriteAsync
Developer Vibration Test UI
~~~

Do not keep private dead methods "just in case".

If a helper is only useful for future diagnostics, make that ownership explicit and call it only from the Developer path.

---

## 20. Preserve Developer diagnostics for A2VM follow-up

Do not delete Developer Menu -> Vibration Test profile diagnostics.

Keep:

~~~text
Apply Left 0 / Right 100
Restore 50 / 50
~~~

and the existing physical Left/Right Test workflow.

These diagnostics remain deliberately separate from production settings.

### 20.1 Current write-probe policy

For this PR:

~~~text
Developer pair-write probe
-> CG3EM only
~~~

because only CG3EM has completed physical acceptance.

Do not silently allow A2VM writes in this production PR.

When A2VM hardware becomes available, extend the Developer probe allowlist in a focused test change and perform the same:

~~~text
0/100
-> Left Test
-> Right Test
-> 50/50 restore
~~~

before enabling A2VM production policy.

### 20.2 Read diagnostic cleanup

The current index-0/index-1 pair-read diagnostic is no longer part of normal Controller-page capture.

Do not leave it silently executing in production UI.

If retained, move it behind an explicit Developer-only action on the existing Vibration Test page and name/log it as a diagnostic.

A dedicated read-only Developer probe is acceptable because it is useful for future A2VM validation.

If moving it would materially broaden this PR, remove the unreachable read diagnostic now and reintroduce it with the A2VM validation PR.

Do not retain dead diagnostic code.

---

## 21. SyncToROM is prohibited in production

This is an explicit invariant.

Search the final vibration production path and prove command 0x22 / SyncToROM is not reachable from:

- startup apply;
- user setting mutation;
- physical recovery;
- resume reapply.

If BuildSyncToRom has no remaining diagnostic use, delete it.

The hardware test proved that live gain change and readback occur without it.

The Addon itself owns durable persistence through settings.json, so ROM persistence is unnecessary.

---

## 22. Logging

Production apply logs should describe desired-state application, not firmware persistence.

Suggested success:

~~~text
ControllerVibrationSettingsApplied
Model=msi.claw.cg3em
Left=30
Right=70
Trigger=Startup|UserMutation|PhysicalRecovery|PowerResume
SyncToRom=False
~~~

Suggested failure:

~~~text
ControllerVibrationSettingsApplyFailed
Reason=...
Left=30
Right=70
Trigger=...
~~~

Do not spam logs on presentation switches because those events perform no write.

Developer probe events keep their explicit Probe naming.

---

## 23. RE document update

Update docs/RE_MSI_ControllerVibration.md.

The current document still says production writes fail closed on every model and describes the CG3EM 0/100 physical effect as unknown.

Update it with the 2026-10-04 acceptance evidence.

Clearly separate:

### PROVEN on CG3EM / MS-1T91 / firmware 0x0419

~~~text
index 1 / relative offset 0x22 / length 2 accepts Left+Right pair

0/100 pair write succeeded with SyncToROM disabled

subsequent pair read returned 0/100

physical Left/Right Test behavior changed as expected in the field test

50/50 restore succeeded with SyncToROM disabled

subsequent pair read returned 50/50
~~~

### Product interpretation

~~~text
Addon persisted pair = desired state
device profile pair = runtime/current applied state
no production ROM commit
~~~

### Still not proven

~~~text
A2VM production support
durable controller-side persistence across power loss
PID1901 production write behavior
universal behavior on every future controller firmware
~~~

Remove stale statements that say CG3EM production pair-write capability is unverified after this production PR lands.

Preserve historical RE context where useful, but label it historical rather than keeping conflicting current policy text.

---

## 24. Tests — settings persistence

Add focused tests:

1. default is exactly 50/50;
2. missing ControllerVibration in old settings JSON -> 50/50;
3. valid custom 0/100 round trips;
4. invalid Left <0 / >100 -> feature-local default/reject according to the selected parser rule;
5. invalid Right <0 / >100 -> feature-local default/reject;
6. malformed vibration settings do not reset LED, button mapping, or unrelated preferences;
7. ChangeControllerVibrationSettings persists before publishing current state.

---

## 25. Tests — protocol and hardware apply

Verify production builder:

~~~text
50/50
-> 0F-00-00-3C-21-01-00-22-02-32-32

0/100
-> 0F-00-00-3C-21-01-00-22-02-00-64

100/0
-> 0F-00-00-3C-21-01-00-22-02-64-00
~~~

Verify:

- exactly 64 bytes;
- values outside 0..100 rejected;
- one pair write only;
- no SyncToROM packet generated;
- no one-byte channel write generated.

Production apply tests:

- CG3EM + Strong exact owned PID1902 identity -> one write;
- A2VM7 -> zero production writes;
- A2VM8 -> zero production writes;
- PID1901 -> zero writes;
- weak identity -> zero writes;
- ambiguous control HID -> zero writes;
- write failure -> false/feature-local failure only.

---

## 26. Tests — frontend/UI semantics

Production capture:

~~~text
persisted settings = 30/70
-> LeftPercent=30
-> RightPercent=70
-> zero profile HID reads
~~~

Set mutation:

~~~text
request 20/80
-> persist 20/80
-> attempt one hardware pair write
~~~

If hardware apply fails:

~~~text
persisted settings remain 20/80
-> returned snapshot remains 20/80
-> failure text says saved but not currently applied
~~~

UI:

- initializes to persisted pair;
- pre-feature settings initialize to 50/50;
- slider move stays debounced;
- ValueChanged itself does not directly call the frontend;
- Test remains independent from strength-apply availability;
- description no longer claims persistent firmware ceiling;
- no Settings-page vibration card is added.

---

## 27. Tests — lifecycle

Mirror the existing Controller LED lifecycle contract.

### Startup

~~~text
physical ownership healthy
-> LED apply
-> vibration apply
-> presentation attach
~~~

Prove vibration apply is after healthy owned PID1902 and before first live presentation.

### Recovery

~~~text
real RecoverLostInputAsync success
-> vibration reapply exactly once from recovery path

RecoveryNotNeeded
-> zero vibration writes

failed recovery
-> zero vibration writes
~~~

### Resume

~~~text
PowerResumeObserved
-> immediate presentation reconcile remains
-> one bounded ~500 ms physical-settings reapply task
-> LED + vibration desired states reapplied
~~~

Do not require physical recovery to run first.

### Presentation-only

Prove:

~~~text
ReconcileControllerPresentationAsync
-> zero vibration setting writes
~~~

### Authority

Center M Enabled / Partial / Unavailable:

~~~text
-> zero production vibration profile writes
~~~

Do not add tests for pathological instruction-level interleavings.

---

## 28. Frontend protocol version

At the reviewed baseline:

~~~text
FrontendTransportProtocol.CurrentVersion = 48
~~~

If the implementation keeps the existing frontend vibration contract shapes and only changes internal semantics, do not bump the protocol merely for semantic cleanup.

If the implementation changes required wire shapes or adds a new Developer read-probe RPC, bump the frontend protocol exactly once:

~~~text
48 -> 49
~~~

Do not bump more than once in this PR.

Prefer the lower-churn route if it remains clean:

~~~text
existing Capture RPC
-> persisted settings/capability projection only
-> zero HID read
~~~

---

## 29. Expected implementation footprint

Likely areas:

~~~text
src/SteamInputAddonforClaw.Contracts/
  ControllerVibration/ControllerVibrationSettings.cs

src/SteamInputAddonforClaw/
  Settings/AppSettings.cs
  Settings/SettingsStore.cs
  Settings/StartupSettingsCoordinator.cs
  Devices/MSI/Claw/MsiClawVibrationProfileCommand.cs
  Devices/MSI/Claw/MsiClawVibrationStrengthClient.cs
  Devices/MSI/Claw/MsiClawVibrationProfilePolicy.cs
  Frontend/InProcessAddonFrontendControl.cs
  Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.UI/
  Views/ControllerPage.xaml
  Views/ControllerPage.xaml.cs
  Views/ControllerVibrationStrengthDebounce.cs
  Views/VibrationTestPage.xaml
  Views/VibrationTestPage.xaml.cs

src/SteamInputAddonforClaw.FrontendTransport/
  only if required by an actual wire-contract change

tests/
  vibration settings/persistence
  profile command/client
  frontend
  UI
  lifecycle

docs/
  RE_MSI_ControllerVibration.md
~~~

Delete obsolete files/tests such as MsiClawVibrationFirmwarePolicy when replaced.

Do not preserve tests whose only purpose is asserting the now-retired fail-closed production architecture.

---

## 30. Explicitly out of scope

Do not:

- move vibration settings to the Settings tab;
- add per-game vibration strength;
- add software rumble multipliers;
- alter runtime XInput/VIIPER rumble values;
- alter physical rumble packet format;
- alter Left/Right Test duration or STOP;
- issue SyncToROM;
- write PID1901 in production;
- enable A2VM production mutation;
- create a new power watcher;
- create a new PnP watcher;
- create a vibration reconcile timer;
- poll/read vibration profile periodically;
- reapply on Steam/BPM presentation changes;
- add manager/state-machine abstractions;
- change Center M authority policy;
- change HidHide ownership;
- change VIIPER lifecycle.

---

## 31. Manual production acceptance

On CG3EM / firmware 0x0419:

### Fresh/default

1. Remove only the vibration property from a test settings file or use a clean settings file.
2. Start Addon under Center M Disabled authority.
3. Confirm Controller > Vibration Strength shows Left 50% / Right 50%.
4. Confirm startup log shows one 50/50 production pair apply after physical ownership.

### User setting

1. Set Left 0 / Right 100.
2. Wait for the existing debounce.
3. Confirm one pair write and no SyncToROM.
4. Press Left Test once.
5. Press Right Test once.
6. Confirm the physical result matches the saved setting.

### Runtime restart

Set a non-default value such as 25/75.

Restart the Addon Runtime normally.

Expected:

~~~text
settings remain 25/75
-> PID1902 ownership established
-> 25/75 reapplied
~~~

### Sleep / Hibernate / Resume

With a non-default pair saved:

1. Sleep and resume.
2. Hibernate and resume if practical.
3. Confirm one post-resume persisted-pair reapply after the bounded HID settle.
4. Confirm controller input/presentation remains healthy.

### PnP / real recovery

If practical, exercise one real physical disappearance/re-enumeration.

Expected:

~~~text
normal Full1902 recovery succeeds
-> persisted vibration pair reapplied
-> presentation resumes
~~~

### Presentation switch

Switch Xbox360 <-> SteamDeck.

Expected:

~~~text
zero vibration setting writes
saved physical strength unchanged
~~~

---

## 32. Definition of done

The production PR is complete when:

~~~text
[ ] Controller UI remains in Controller > Vibration Strength.
[ ] Default persisted pair is exactly 50/50.
[ ] App settings are the sole durable vibration-strength authority.
[ ] Normal Controller-page capture performs zero vibration-profile HID reads.
[ ] User edits persist before best-effort hardware apply.
[ ] Production apply is exactly one index1/0x22/len2 pair write.
[ ] Production apply issues no SyncToROM.
[ ] Old one-byte channel mutation path is removed.
[ ] Old production readback-verification dependency is removed.
[ ] CG3EM is production-enabled.
[ ] A2VM remains production-disabled.
[ ] Initial PID1902 ownership reapplies persisted vibration.
[ ] Successful real physical recovery reapplies persisted vibration.
[ ] Sleep/Hibernate Resume reapplies persisted vibration after bounded settle.
[ ] Presentation-only reconcile performs zero vibration writes.
[ ] Apply failure never tears down Full1902 ownership or rolls persisted settings back.
[ ] Physical Left/Right Test path remains unchanged.
[ ] Developer Vibration Test diagnostics remain available for future A2VM work.
[ ] Obsolete firmware-address policy/readback/SyncToROM production code is removed.
[ ] RE_MSI_ControllerVibration.md reflects CG3EM production evidence and remaining A2VM unknowns.
[ ] Focused regression tests pass.
~~~

---

## 33. Final intended architecture

~~~text
Controller page
    |
    | Left / Right 0..100
    v
settings.json
ControllerVibrationSettings
(default 50 / 50)
    |
    | one desired-state authority
    v
ApplyOwnedControllerVibrationSettingsAsync
    |
    | Center M Disabled
    | healthy owned PID1902
    | Strong exact physical identity
    | CG3EM production policy
    v
MsiClawVibrationStrengthClient
    |
    | exactly one write
    v
0F 00 00 3C 21 01 00 22 02 <L> <R>
    |
    +-- no SyncToROM
    +-- no production readback requirement
~~~

Lifecycle call sites:

~~~text
initial ownership success
user setting commit
real physical recovery success
Power Resume after bounded settle
~~~

No other reconcile loop is required.

The objective is one persisted desired pair, one exact physical apply path, and the existing Full1902 lifecycle as the only recovery authority.
