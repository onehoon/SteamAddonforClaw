# Work Order — Hide Auxiliary PID1902 HID Projections from Steam with an Exact Full1902 HidHide Target Set

## Status

Production Full1902 isolation/polish work order.

This PR changes the persistent Full1902 HidHide owned set from:

```text
required exact primary PID1902 gamepad collection only
```

to:

```text
required exact primary PID1902 gamepad collection
+ exact PID1902 control HID collection when present and uniquely resolved
+ exact PID1902 consumer-control HID collection when present and uniquely resolved
```

The user-facing goal is simple:

> While Addon controller authority is active, Steam's Controller page must not expose the physical MSI Claw as mysterious extra `Љ` controller entries. Only the Addon's intended virtual presentation should be user-visible as a controller.

This is **not** a broad VID/PID hide, not a controller-routing redesign, and not a new authority layer.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     ab80a224c0a38006cca59afd21431f43f3a271ba
```

Before implementation, read and treat these as active design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/work-order/PR2_ADDON_OWNED_HIDHIDE_BASELINE_WORK_ORDER.md`
- `docs/work-order/PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md`
- `docs/work-order/PR8_OWNED_DIRECTINPUT_SESSION_RECOVERY_WORK_ORDER.md`
- `docs/work-order/PR10_PHYSICAL_DEVICE_LOSS_PNP_RETURN_RECOVERY_WORK_ORDER.md`
- `docs/work-order/PR10_HIDHIDE_STARTUP_AUTHORITY_ADDENDUM.md`
- `docs/work-order/PR12_STOCK_SAFE_UNINSTALL_CORE_WORK_ORDER.md`
- `docs/work-order/ENVIRONMENT_DISCOVERY_WINDOWS_INPUT_BACKEND_ENUMERATION_WORK_ORDER.md`

Inspect the current source before editing:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/
  MsiClawHardware.cs
  MsiClawAddonPhysicalOwnership.cs
  MsiClawModeController.cs
  WindowsMsiClawModeWriter.cs
  MsiClawRumbleEndpointResolver.cs
  WindowsMsiClawRumbleEndpointCatalog.cs
  MsiClawFrontButtonRuntime.cs

src/SteamInputAddonforClaw/HidHide/
  AddonControllerHidHideBaseline.cs
  HidHideDriverClient.cs

src/SteamInputAddonforClaw/Startup/
  AddonStartupComposition.cs

src/SteamInputAddonforClaw/Hosting/
  AddonProcessHost.cs

src/SteamInputAddonforClaw/CenterMStartup/
  CenterMRebootAuthorityTransition.cs

tests/SteamInputAddonforClaw.Tests/
  AddonControllerHidHideBaselineTests.cs
  DisabledBootControllerAdmissionTests.cs
  MsiClawAddonPhysicalOwnershipTests.cs
  CenterMRebootAuthorityTransitionTests.cs
```

The application is pre-release. Prefer the clean current contract over compatibility wrappers for obsolete internal APIs.

---

# 1. Hardware evidence

The trigger for this PR is not double input.

The product problem is that Steam Controller settings exposes two unexplained MSI physical-controller entries whose product string renders as `Љ`.

The previous Steam controller log showed two local devices with the same physical MSI VID/PID:

```text
type: 0db0 1902
Product: Љ

type: 0db0 1902
Product: Љ
```

The new post-PR525 hardware report:

```text
EnvironmentDiscovery-20260918-173153.log
SnapshotVersion: 3
CapturedAt: 2026-09-18T17:31:53.7931686+09:00
AppVersion: 0.1.235.0
Steam running: yes
```

shows the following.

## 1.1 Present MSI PID1902 PnP topology

Exactly one current physical root:

```text
USB\VID_0DB0&PID_1902\5&3AF6909B&0&4
```

and these exact HID collections:

```text
MI_00/COL01
  HID\VID_0DB0&PID_1902&MI_00&COL01\7&F02B9F1&0&0000
  UsagePage=0001
  Usage=0005
  HID_DEVICE_SYSTEM_GAME

MI_00/COL02
  HID\VID_0DB0&PID_1902&MI_00&COL02\7&F02B9F1&0&0001
  UsagePage=FFF0
  Usage=0040
  vendor/control HID

MI_01/COL01
  keyboard
  UsagePage=0001
  Usage=0006

MI_01/COL02
  mouse
  UsagePage=0001
  Usage=0002

MI_01/COL03
  HID\VID_0DB0&PID_1902&MI_01&COL03\7&32D9F7B3&0&0002
  UsagePage=000C
  Usage=0001
  consumer control
```

## 1.2 DirectInput evidence

DirectInput reported exactly:

```text
045E:028E -> one VIIPER Xbox360
0DB0:1902 -> one MSI gamepad
              MI_00/COL01
              Usage 0001/0005
              17 buttons
              6 axes
              VerifiedMsiPhysicalRoot
```

There was no duplicate physical PID1902 DirectInput gamepad.

## 1.3 Raw Input evidence

Raw Input reported exactly three retained controller-relevant projections:

```text
045E:028E
  IG_01
  Usage 0001/0005

0DB0:1902
  MI_00/COL02
  Usage FFF0/0040

0DB0:1902
  MI_01/COL03
  Usage 000C/0001
```

The two physical `0DB0:1902` Raw Input projections match the count of the two mysterious physical `Љ` entries previously observed by Steam.

This is strong correlation, but the implementation must still use the post-change Steam hardware validation below to prove that hiding these exact two auxiliary collections actually removes the UI entries.

## 1.4 GameInput evidence

GameInput reported only:

```text
045E:028E
DeviceFamily=Xbox360
SupportedInput=Gamepad
Connected
```

It did not report a second Xbox360 and did not report PID1902 as a GameInput gamepad.

---

# 2. Product decision

When Full1902 Addon authority is active:

```text
physical MSI gamepad             -> hidden from non-whitelisted apps
physical MSI control HID         -> hidden from non-whitelisted apps when exactly resolved
physical MSI consumer-control HID-> hidden from non-whitelisted apps when exactly resolved
virtual Xbox360 / SteamDeck      -> visible
```

The intent is that Steam Controller settings exposes only the intended Addon virtual controller presentation and no confusing physical `Љ` entries.

This does **not** mean hiding every PID1902 child.

Do not hide:

```text
MI_01/COL01 keyboard
MI_01/COL02 mouse
USB\VID_0DB0&PID_1902 root
MI_00 parent/interface root
MI_01 parent/interface root
wildcard VID/PID matches
```

Only exact current HID collection instance IDs may be written into HidHide.

---

# 3. Required versus optional targets

This distinction is critical for compatibility across supported MSI Claw models / firmware.

## 3.1 Required target

The primary DirectInput gamepad remains mandatory:

```text
MI_00/COL01
VID=0DB0
PID=1902
UsagePage=0001
Usage=0005 or the already-supported joystick form
same strongly verified physical PID1902 identity
```

If the exact primary gamepad cannot be proven, physical ownership still fails closed exactly as today.

## 3.2 Auxiliary targets

The following are additional isolation targets **only when each is currently present and uniquely resolved under the same physical PID1902 root**:

```text
Control:
  MI_00/COL02
  UsagePage=FFF0
  Usage=0040

Consumer:
  MI_01/COL03
  UsagePage=000C
  Usage=0001
```

Do not make the existence of either auxiliary collection a prerequisite for supported controller ownership.

Reason:

- the current hardware capture proves both on the tested system;
- the repository proves `FFF0/0040` is a real PID1902 control HID;
- the repository does not yet prove that every supported Claw model/firmware exposes the exact same consumer collection topology;
- a UI-cleanup target must not make an otherwise valid controller unusable on another supported device.

If an auxiliary kind is absent:

```text
0 matching exact candidates
-> omit that auxiliary target
-> continue with the required primary target
```

If an auxiliary kind is ambiguous:

```text
>1 matching exact candidates
-> do not guess
-> do not broad-hide
-> log the ambiguity
-> continue with the proven target set
```

The primary target remains the safety gate.

---

# 4. Why these hides must not break current features

The implementation must preserve these code facts.

## 4.1 DirectInput controller input

The live physical controller source is:

```text
MI_00/COL01
UsagePage=0001
Usage=0005
```

This collection is already hidden today.

The Addon Runtime remains in the HidHide application whitelist, so the Addon continues to access its own hidden physical input.

No change to:

- sticks;
- D-pad;
- ABXY;
- triggers;
- M1/M2;
- DirectInput acquisition;
- publisher input mapping.

## 4.2 Rumble

Current `MsiClawRumbleEndpointResolver` deliberately selects the verified PID1902 **gamepad** endpoint:

```text
VID=0DB0
PID=1902
InputReportLength=64
OutputReportLength>0
UsagePage=0001
Usage=0005 or joystick usage
same strong physical identity
OpenSucceeded=true
```

It does not use `FFF0/0040` as the rumble endpoint.

The selected gamepad collection is already hidden today, and rumble works because the Addon Runtime is whitelisted.

Do not change rumble endpoint selection in this PR.

## 4.3 PID1901 <-> PID1902 native mode switching

The repository confirms the PID1902 control HID is:

```text
UsagePage=FFF0
Usage=0040
```

and the native mode writer depends on that control HID.

Hiding this exact collection is allowed only because the process that opens it is the whitelisted Addon Runtime.

Do not change:

- `MsiClawModeController`;
- `MsiClawControlHidResolver`;
- `WindowsMsiClawModeWriter`;
- mode packets;
- transition timing.

Add tests proving the whitelist/baseline contract still includes the current Runtime before the new hidden targets become authoritative.

## 4.4 WING / OEM1

Current production front-button handling is not sourced from the PID1902 consumer HID.

`MsiClawFrontButtonRuntime` uses:

```text
WmiMsiEventSource
root\WMI
MSI_Event
Event88 -> WING
Event41 -> OEM1
```

Therefore this PR must not change front-button acquisition or mapping.

Manual hardware validation still must press both buttons after the hide expansion.

## 4.5 Gyro / accelerometer

Current gyro research/diagnostics source is separate from PID1902:

```text
Intel ISH
VID_8087 PID_0AC2
ST_MICRO LSM6DSO
Physical Gyrometer
Physical Accelerometer
Legacy Sensor API
```

Do not modify sensor/gyro code in this PR.

The PID1902 auxiliary HidHide change must not touch the Intel sensor topology.

---

# 5. One owner, one exact target-set contract

Do not create a second HidHide manager.

Keep:

```text
AddonControllerHidHideBaseline
```

as the single persistent HidHide authority.

Its underlying:

```text
ApplyDisabledModeBaseline(IReadOnlyCollection<string>)
ApplyEnabledModeBaseline(IReadOnlyCollection<string>)
```

already support multiple exact targets.

The main work is to remove the **single-target assumptions around those generic primitives**.

Current single-target assumptions include:

```text
ApplyDisabledModeBaselineNormalizingExistingOwnedTarget(...)
TryGetSingleExistingOwnedTarget(...)
MsiClawAddonPhysicalOwnership._ownedHiddenTarget
Func<string, AddonHidHideBaselineResult> _applyHidHideTarget
Func<string?> _captureExistingOwnedHiddenTarget
PhysicalOwnershipReleaseResult.HiddenTarget
CenterMRebootAuthorityTransition captureExistingOwnedHiddenTarget
CenterM release [target]
```

These must converge on one exact owned-target-set contract.

---

# 6. Hardware classification helpers

Update:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawHardware.cs
```

Keep the existing primary helper, but add narrow classification for the two auxiliary collection shapes.

Suggested constants:

```csharp
public const ushort DirectInputControlUsagePage = 0xFFF0;
public const ushort DirectInputControlUsage = 0x0040;
public const ushort ConsumerUsagePage = 0x000C;
public const ushort ConsumerUsage = 0x0001;

public const string DirectInputControlHidCollectionPrefix =
    "HID\\VID_0DB0&PID_1902&MI_00&COL02\\";

public const string ConsumerHidCollectionPrefix =
    "HID\\VID_0DB0&PID_1902&MI_01&COL03\\";
```

A tiny enum is acceptable:

```csharp
internal enum MsiClawHidHideTargetKind
{
    PrimaryGamepad,
    Control,
    Consumer
}
```

Add a narrow classifier equivalent to:

```csharp
internal static bool TryClassifyOwnedPid1902HidHideTarget(
    string? instanceId,
    out MsiClawHidHideTargetKind kind)
```

It should classify only these exact instance-ID shapes.

Do not classify:

- keyboard;
- mouse;
- USB root;
- all `0DB0:1902` devices;
- arbitrary HIDClass children.

---

# 7. Resolve the current exact target set from one PnP snapshot

Physical ownership currently has only an individual:

```text
resolvePnpDevice(instanceId)
```

callback.

To resolve adjacent exact collections safely, provide the physical owner with one current PnP inventory callback:

```csharp
Func<IReadOnlyList<ControllerDeviceInfo>> enumeratePnpDevices
```

Reuse the existing `WindowsControllerDeviceEnumerator` instance in `AddonProcessHost`.

Do not create another PnP watcher or manager.

A small pure helper may live in `MsiClawHardware` or next to `MsiClawAddonPhysicalOwnership`.

Preferred behavior:

```text
input:
  one PnP snapshot
  verified primary DirectInput PnP instance
  verified strong PID1902 physical identity/root

required:
  exact primary target == descriptor.PnpInstanceId

optional search:
  present VID 0DB0 / PID 1902
  same strong physical identity/root
  exact Control classification
  exact Consumer classification

output:
  deterministic exact target list
```

Ordering must be stable:

```text
1. PrimaryGamepad
2. Control
3. Consumer
```

Example for the 2026-09-18 hardware:

```text
HID\VID_0DB0&PID_1902&MI_00&COL01\7&F02B9F1&0&0000
HID\VID_0DB0&PID_1902&MI_00&COL02\7&F02B9F1&0&0001
HID\VID_0DB0&PID_1902&MI_01&COL03\7&32D9F7B3&0&0002
```

A matching VID/PID target under another physical root must never be included.

---

# 8. Auxiliary ambiguity policy

Do not add a complex result hierarchy.

The resolver only needs to return:

```text
exact target list
+ optional diagnostic reason(s) for omitted auxiliary kinds
```

or log omission directly at the call site.

Policy:

### Primary

```text
missing / mismatched / ambiguous
-> physical ownership failure
```

This preserves the existing safety boundary.

### Control / Consumer

```text
exactly one same-root candidate
-> include

zero
-> omit

more than one
-> omit and warn
```

Do not fail the whole controller session solely because an auxiliary cosmetic-isolation target is missing or ambiguous.

---

# 9. Persisted Disabled-boot baseline migration

The current startup path preserves at most one persisted primary target:

```text
ApplyDisabledModeBaselineNormalizingExistingOwnedTarget(...)
```

Replace the single-target concept with a persisted owned-set selector.

A clean shape is:

```csharp
internal AddonHidHideBaselineResult
    ApplyDisabledModeBaselineNormalizingExistingOwnedTargets(
        Func<IReadOnlyList<string>, IReadOnlyList<string>> selectOwnedTargets)
```

The baseline:

1. inspects current hidden entries;
2. normalizes them;
3. passes the normalized list to the supplied pure selector;
4. applies the Disabled baseline using exactly the returned set;
5. removes every other hidden entry as it already does.

Similarly replace:

```text
TryGetSingleExistingOwnedTarget(...)
```

with a plural read-only primitive such as:

```csharp
internal IReadOnlyList<string> TryGetExistingOwnedTargets(
    Func<IReadOnlyList<string>, IReadOnlyList<string>> selectOwnedTargets)
```

The returned set is valid only when:

```text
InspectDisabledModeBaseline(selectedTargets)
-> AlreadyCompliant
```

Otherwise return an empty list.

Do not expose raw HidHide inspection to other owners.

---

# 10. Persisted-set selector rules

Add one small MSI-specific selector outside `AddonControllerHidHideBaseline`.

The generic HidHide baseline must not learn MSI topology.

The selector should accept these upgrade/persistence shapes:

```text
primary only
primary + control
primary + consumer
primary + control + consumer
```

provided:

- there is exactly one primary target;
- there is at most one control target;
- there is at most one consumer target;
- every preserved target matches one of the three exact MSI owned collection shapes.

This intentionally supports migration from existing installed builds that persisted only the primary target.

Examples:

```text
[primary]
-> preserve [primary]

[primary, control, consumer]
-> preserve all three

[foreign, primary, control]
-> selector returns [primary, control]
-> generic baseline removes foreign

[two different primary targets]
-> selector returns []
-> normalize to zero-target foundation

[control, consumer] with no primary
-> selector returns []
-> normalize to zero-target foundation
```

Do not attempt to infer physical-root equality from persisted HID strings alone.

Current-world same-root proof belongs to physical ownership when it resolves the live target set.

The zero-target foundation remains an explicitly valid pre-ownership state.

---

# 11. Physical ownership state

Update:

```text
MsiClawAddonPhysicalOwnership
```

without adding another owner.

Keep the primary target concept because recovery still uses it as the exact DirectInput collection identity.

Recommended fields:

```csharp
private string? _ownedHiddenTarget; // primary only, existing recovery identity
private IReadOnlyList<string> _ownedHiddenTargets = []; // complete exact set
```

This is preferable to inventing another manager/state machine.

Rename the first field to `_ownedPrimaryHiddenTarget` if doing so makes the implementation clearer; consistency matters more than preserving an internal name.

Change constructor seams from:

```text
Func<string, AddonHidHideBaselineResult> applyHidHideTarget
Func<string?> captureExistingOwnedHiddenTarget
```

to plural:

```text
Func<IReadOnlyCollection<string>, AddonHidHideBaselineResult> applyHidHideTargets
Func<IReadOnlyList<string>> captureExistingOwnedHiddenTargets
Func<IReadOnlyList<ControllerDeviceInfo>> enumeratePnpDevices
```

The existing DirectInput enumeration remains unchanged.

---

# 12. Acquisition ordering

Preserve the current safety order:

```text
PID1902 strong identity
-> exact DirectInput descriptor
-> exact primary PnP proof
-> DirectInput StartPrepared
-> first valid state
-> HidHide exact-set reconciliation
-> physical ownership committed
-> virtual presentation may later attach
```

After the first valid DirectInput state, resolve the complete exact HidHide set from a current PnP snapshot.

Required:

```text
targetSet contains descriptor.PnpInstanceId as PrimaryGamepad
```

Then:

```csharp
_ownedHiddenTarget = primary;
_ownedHiddenTargets = targetSet;
baseline = _applyHidHideTargets(targetSet);
```

Store the exact set **before** applying HidHide, just as the existing code stores the primary target before a possibly-partial mutation.

Reason:

If HidHide partially mutates and verification fails, the later Center M Enable / uninstall release path must still know every Addon target it attempted to own.

On success:

```text
_ownedHiddenTarget  = primary
_ownedHiddenTargets = exact resolved set
```

Add useful logging:

```text
Event=PhysicalIsolationVerified
PrimaryHiddenTarget=<...COL01...>
HiddenTargetCount=1..3
ControlHidden=true|false
ConsumerHidden=true|false
HidHideOutcome=...
```

Do not log button values or live input.

---

# 13. Recovery behavior

Keep existing primary-target recovery semantics.

The primary DirectInput collection remains the identity-critical target.

Existing rule remains:

```text
recovered primary target != committed primary target
-> RecoveredTargetChanged
-> fail closed
```

Do not use this PR to implement primary target migration.

Once the same primary target is proven, re-resolve the current auxiliary set from the fresh current PnP snapshot and same strong physical identity.

Then apply:

```text
[current same primary + current uniquely resolved auxiliaries]
```

before restarting DirectInput.

This allows realistic auxiliary PnP re-enumeration to converge without weakening the existing primary ownership contract.

Only auxiliary exact entries may be replaced this way.

Do not migrate the primary target in this PR.

After successful baseline verification, update:

```text
_ownedHiddenTargets = currentExactSet
```

Then restart the same DirectInput source exactly as today.

No retry manager, epoch, or additional recovery owner is needed.

---

# 14. Center M Enable / stock restoration / uninstall

This is the most important lifecycle change after acquisition.

Current release returns only:

```text
PhysicalOwnershipReleaseResult.HiddenTarget
```

That is no longer sufficient.

Change the release result to carry the full exact set, for example:

```csharp
internal sealed record PhysicalOwnershipReleaseResult(
    bool Succeeded,
    string Reason,
    IReadOnlyList<string> HiddenTargets)
{
    internal static PhysicalOwnershipReleaseResult NothingOwned { get; } =
        new(true, "NoPhysicalOwnership", []);
}
```

The live owner must return:

```text
_ownedHiddenTargets
```

or, when no current-process set was committed, the plural persisted exact set recovered through the baseline.

Important ordering remains:

```text
stop process-owned DirectInput
-> restore physical MSI Claw to PID1901
-> independently prove stock PID1901
-> remove ALL exact Addon-owned HidHide targets
-> remove Addon whitelist entry / Active=false
-> enable Center M roots
```

Even though the PID1902 PnP nodes disappear after mode restoration, their exact strings must remain available in `HiddenTargets` so the persistent HidHide blacklist can be cleaned.

Update `CenterMRebootAuthorityTransition` from:

```text
release.HiddenTarget ?? captureExistingOwnedHiddenTarget()
ApplyEnabledModeBaseline([target])
```

to plural:

```text
release.HiddenTargets if non-empty
else captureExistingOwnedHiddenTargets()

ApplyEnabledModeBaseline(targets)
```

Do not fall back to broad PID1902 removal.

The uninstall path shares this same core and must receive the same change.

---

# 15. AddonStartupComposition

Update the Disabled-boot admission composition.

Current:

```csharp
ApplyDisabledModeBaselineNormalizingExistingOwnedTarget(
    MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId)
```

Replace it with the plural owned-set selector.

The startup baseline must support:

```text
zero-target foundation
old primary-only persisted baseline
new primary + auxiliary persisted baseline
```

Do not require current PnP enumeration during this early baseline normalization.

The authoritative current-world target set is established later by physical ownership.

---

# 16. AddonProcessHost composition

Reuse the existing:

```text
WindowsControllerDeviceEnumerator controllerDevices
```

already created in `CreatePhysicalOwnership`.

Pass:

```csharp
() => controllerDevices.EnumeratePresentDevices()
```

to the physical owner.

Do not instantiate a second enumerator for the auxiliary target set.

Change:

```csharp
target => hidHideBaseline.ApplyDisabledModeBaseline([target])
```

to:

```csharp
targets => hidHideBaseline.ApplyDisabledModeBaseline(targets)
```

Change the persisted capture to the plural baseline primitive + MSI persisted-set selector.

---

# 17. No change to virtual presentation

Do not modify:

- `CanonicalViiperRuntime`;
- Xbox360 device creation;
- SteamDeck device creation;
- presentation switching;
- publisher;
- Steam/BPM observation;
- QAM;
- Overlay input routing.

The expected user-visible result is achieved only by removing unintended physical PID1902 projections from non-whitelisted consumers.

There must still be exactly one intended virtual presentation.

---

# 18. No change to rumble

Do not alter:

```text
MsiClawRumbleSink
MsiClawRumbleEndpointResolver
WindowsMsiClawRumbleEndpointCatalog
MsiClawRumblePacketBuilder
```

The post-change hardware test must prove rumble still works while all resolved physical target collections are hidden.

---

# 19. No change to front-button or gyro systems

Do not alter:

```text
MsiClawFrontButtonRuntime
WmiMsiEventSource
Wing*
Oem1*
ClawSensorProbe*
gyro/sensor production or diagnostic code
```

The hardware test verifies these behaviors; it does not redesign them.

---

# 20. Active architecture documentation

This PR changes a core Full1902 contract.

Update active docs that currently say:

```text
exact primary PID1902 gamepad collection only
one hidden target
```

At minimum review/update:

```text
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
```

New wording should distinguish:

```text
required primary safety target
optional exact auxiliary visibility-suppression targets
```

Do not rewrite historical work orders.

---

# 21. Tests — target classification and resolution

Add focused tests for the pure target resolver.

## 21.1 Exact current hardware set

Input one physical root with:

```text
COL01 0001/0005
COL02 FFF0/0040
COL03 000C/0001
keyboard 0001/0006
mouse 0001/0002
```

Expect exactly:

```text
COL01
COL02
COL03
```

in stable order.

## 21.2 Never hide keyboard/mouse

Assert:

```text
MI_01/COL01 keyboard -> excluded
MI_01/COL02 mouse    -> excluded
```

## 21.3 Different physical root

Add another `0DB0:1902` control/consumer target under another strong physical identity.

Expect it to be excluded.

## 21.4 Missing auxiliary

Primary only:

```text
-> valid [primary]
```

Primary + control only:

```text
-> valid [primary, control]
```

Do not fail ownership.

## 21.5 Ambiguous auxiliary

Two control candidates under the same root:

```text
-> do not guess either control target
-> primary remains usable
```

Same for consumer.

---

# 22. Tests — persisted HidHide set

Update `AddonControllerHidHideBaselineTests`.

Required cases:

### Old build upgrade

```text
Hidden=[primary]
-> startup normalizer preserves primary
-> compliant
```

### New complete set

```text
Hidden=[primary, control, consumer]
-> startup normalizer preserves all three
-> no mutation when otherwise compliant
```

### Foreign entries

```text
Hidden=[foreign, primary, control, consumer]
-> foreign removed
-> exact owned set preserved
```

### Partial valid set

```text
Hidden=[primary, control]
-> preserve both
```

### Invalid set

```text
Hidden=[control, consumer] // no primary
-> normalize to zero-target foundation
```

### Ambiguous primary

```text
Hidden=[primary-generation-A, primary-generation-B, control]
-> normalize to zero-target foundation
```

### Enabled release

```text
ApplyEnabledModeBaseline([primary, control, consumer])
-> all three removed
-> Addon application removed
-> Active=false
-> read-back verified
```

---

# 23. Tests — physical acquisition

Update `MsiClawAddonPhysicalOwnershipTests`.

## 23.1 Full tested topology

Acquisition with the current hardware topology must call HidHide once with:

```text
[primary, control, consumer]
```

and commit the same set.

## 23.2 Auxiliary absence

A valid primary with no consumer target must still acquire successfully.

The baseline gets only the exact targets actually proven.

## 23.3 No broad target

Assert the applied set contains no:

```text
USB\VID_0DB0&PID_1902...
MI_01/COL01
MI_01/COL02
```

## 23.4 Partial HidHide apply failure

If the multi-target baseline mutation/verification fails after the owner has resolved the set:

- acquisition fails;
- DirectInput process-owned source is stopped;
- the owner retains enough exact target-set evidence for a later explicit Center M Enable / uninstall release;
- no PID1901 automatic rollback occurs while Center M remains Disabled.

Preserve current fail-close semantics.

---

# 24. Tests — recovery

Required:

### Same primary, same auxiliary set

```text
-> normal recovery
-> same target set reverified
```

### Same primary, refreshed auxiliary instance

Example:

```text
primary unchanged
old consumer instance disappears
new same-root exact consumer instance appears
```

Expect:

```text
primary remains authoritative
new exact auxiliary set reconciled
_ownedHiddenTargets updated after verification
DirectInput restarts
```

### Primary changes

Preserve existing behavior:

```text
recovered primary != committed primary
-> RecoveredTargetChanged
-> no primary target migration
```

Do not relax this in this PR.

---

# 25. Tests — Center M Enable and uninstall

Update `CenterMRebootAuthorityTransitionTests`.

Required:

### Live owner release

```text
PhysicalOwnershipReleaseResult.HiddenTargets
= [primary, control, consumer]

-> stock PID1901 proof
-> ApplyEnabledModeBaseline(all three)
-> all three absent
-> Center M Enabled
```

### Blocked/no current owner

Persisted:

```text
[primary, control, consumer]
```

must be recovered by the plural persisted-set reader and all removed.

### Old primary-only persisted build

Persisted:

```text
[primary]
```

must still be releasable.

### Release failure

If any requested exact target cannot be removed/read-back verified:

```text
-> Center M enable remains blocked
-> startup task removal remains blocked for uninstall
```

Preserve the existing stock-restoration safety order.

---

# 26. Runtime logging

Update logs from singular-only wording where needed.

Prefer:

```text
PrimaryHiddenTarget
HiddenTargetCount
HiddenTargets
ControlHidden
ConsumerHidden
```

Do not produce enormous repeated lists on every input event.

These logs are lifecycle/ownership events only.

Useful events:

```text
PhysicalIsolationVerified
PhysicalOwnershipReleased
OwnedPhysicalRecoveryIsolationVerified
UninstallHidHideRelease
```

---

# 27. Hardware validation

This is mandatory before treating the UI issue as fixed.

Use the same MSI Claw and Steam client where the two `Љ` entries are visible before the change.

## 27.1 Before change

Capture/confirm:

```text
Steam Settings -> Controller:
  intended virtual controller
  Љ
  Љ
```

Preserve:

```text
Steam\logs\controller.txt
Steam\logs\controller_ui.txt
Addon log
Environment Discovery
```

## 27.2 After change

Boot/restart into normal Full1902 Addon authority.

Confirm HidHide exact target set:

```text
primary COL01 hidden
control COL02 hidden when present
consumer COL03 hidden when present
no keyboard/mouse/root hidden
Addon Runtime whitelisted
Active=true
Inverse=false
```

Then open Steam Controller settings.

Expected:

```text
no physical Љ entries
exactly one intended current virtual presentation
```

If `Љ` remains, do **not** broaden HidHide further.

Capture a fresh:

```text
Environment Discovery
controller.txt
controller_ui.txt
Addon log
```

and stop. The correlation hypothesis would need revision.

---

# 28. Functional regression validation

On the same hardware, validate all of the following after the expanded hide set is active.

## Controller input

- sticks;
- D-pad;
- ABXY;
- triggers;
- M1;
- M2.

## Virtual presentation

- Xbox360 normal presentation;
- SteamDeck presentation when expected;
- no extra virtual controller.

## Rumble

- normal game rumble / test rumble;
- both motors where test path supports them;
- no rumble loss after Xbox360 <-> SteamDeck presentation switch.

## Front buttons

- WING action;
- OEM1 action;
- Steam button pulse mapping;
- QAM/Quick Access pulse mapping where configured;
- Addon Overlay mapping where configured.

## Native mode / authority

- already-PID1902 startup;
- PID1901 -> PID1902 transition;
- explicit Center M Enable and Restart:
  - PID1902 -> PID1901 restoration;
  - all Addon hidden targets removed;
  - HidHide Active=false;
  - Center M roots Enabled/Enabled/Automatic.

## Power / lifecycle

- Sleep -> Resume;
- one physical device-loss / PnP return if practical;
- normal Addon process restart while Center M remains Disabled.

## Sensor / gyro isolation

Run the existing developer Sensor Probe / Environment Discovery and confirm the Intel ISH / LSM6DSO sensor evidence is unchanged.

No new gyro code is required.

---

# 29. Expected files

Likely modified:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawHardware.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs
src/SteamInputAddonforClaw/HidHide/AddonControllerHidHideBaseline.cs
src/SteamInputAddonforClaw/Startup/AddonStartupComposition.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/CenterMStartup/CenterMRebootAuthorityTransition.cs

tests/SteamInputAddonforClaw.Tests/AddonControllerHidHideBaselineTests.cs
tests/SteamInputAddonforClaw.Tests/DisabledBootControllerAdmissionTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawAddonPhysicalOwnershipTests.cs
tests/SteamInputAddonforClaw.Tests/CenterMRebootAuthorityTransitionTests.cs

docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
```

A separate new manager/interface is not expected.

A tiny enum / pure resolver helper is acceptable if it keeps the target classification readable.

---

# 30. Explicit non-goals

Do not:

- hide the whole PID1902 device;
- hide every PID1902 HID child;
- hide keyboard/mouse collections;
- change VIIPER;
- change Steam/SDL;
- change DirectInput polling;
- change rumble packet format or endpoint policy;
- change M1/M2 mapping;
- change WING/OEM1 acquisition;
- change gyro/sensor implementation;
- add a retry state machine;
- add a new HidHide owner;
- add a second PnP watcher;
- add an epoch/barrier/manager solely for theoretical interleavings;
- implement primary HidHide target migration;
- add support for unsupported multi-session/RDP/Fast User Switching scenarios.

---

# 31. Race / overengineering policy

This PR must handle real lifecycle behavior:

- restart;
- crash/relaunch with persistent HidHide state;
- Sleep/Resume;
- physical controller loss / PnP return;
- explicit Center M Enable / stock restoration;
- actual HidHide mutation/read-back failure.

Do not add machinery for instruction-level theoretical races.

The existing:

```text
physical owner gate
persistent HidHide owner
PnP recovery
stock restoration ordering
```

remain sufficient authorities.

The new auxiliary targets are data owned by those existing paths, not a reason to create another lifecycle subsystem.

---

# 32. Build / validation

Required automated validation:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet test SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
```

Use the current repository-normal commands if CI uses equivalent switches.

No new warnings.

---

# 33. Acceptance criteria

This PR is complete when:

1. The primary PID1902 gamepad remains a required exact HidHide target.
2. The exact same-root PID1902 control HID is added when uniquely present.
3. The exact same-root PID1902 consumer-control HID is added when uniquely present.
4. Auxiliary absence does not block valid physical controller ownership.
5. Auxiliary ambiguity is never guessed or broad-hidden.
6. Keyboard and mouse collections are never added.
7. No PID1902 root/wildcard hide is introduced.
8. The Addon Runtime remains whitelisted.
9. DirectInput input remains functional.
10. M1/M2 remain functional.
11. Rumble remains functional through the existing gamepad HID endpoint.
12. PID1901 <-> PID1902 mode switching remains functional through the whitelisted Runtime.
13. WING/OEM1 remain functional through the existing WMI event path.
14. Gyro/accelerometer sensor discovery remains unchanged.
15. Existing primary-only persisted installations migrate without manual cleanup.
16. The new exact multi-target set persists across ordinary Addon restart / Windows reboot while Center M remains Disabled.
17. Recovery keeps the primary target identity rule and may reconcile only current exact auxiliary targets.
18. Center M Enable removes the complete exact Addon target set before returning stock authority.
19. Uninstall stock preparation removes the complete exact Addon target set.
20. HidHide mutation/read-back failure remains fail-closed.
21. No second HidHide/PnP/controller authority is added.
22. Full automated tests pass.
23. On the affected hardware, Steam Controller settings no longer displays the two physical `Љ` entries.
24. Steam still shows exactly the intended current virtual controller presentation.
25. A fresh post-change Discovery confirms no unintended physical collections were hidden.

---

# 34. Final implementation principle

Do not solve a cosmetic Steam UI problem with a broad physical-device hide.

Use the evidence already collected:

```text
one real PID1902 DirectInput gamepad
two auxiliary PID1902 RawInput projections
two mysterious physical PID1902 Steam entries
```

and extend the **existing** Full1902 HidHide owner to the smallest exact current target set:

```text
Primary gamepad  -> required
Control HID      -> optional exact hide when uniquely proven
Consumer control -> optional exact hide when uniquely proven
```

The Addon remains the one whitelisted physical-controller owner.

Steam and games see only the intended virtual controller presentation.
