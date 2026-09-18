# Work Order — Extend Environment Discovery with Windows Controller-Backend Enumeration

## Status

Focused developer-diagnostic work order.

This PR extends the existing **Environment Discovery Report** with one-shot controller enumeration evidence from:

- DirectInput;
- Windows Raw Input;
- Microsoft GameInput.

The purpose is to determine where the currently observed Steam controller duplication begins.

This is **not** a HidHide-policy change, not a controller-routing change, not a presentation change, and not a new controller authority layer.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     14e57e2b7b733def2219e71e92883021cebe08e7
```

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/PR5_PID1902_DIRECTINPUT_PHYSICAL_OWNERSHIP_WORK_ORDER.md`
- `docs/gyro/SD6A_ENVIRONMENT_DISCOVERY_SENSOR_EVIDENCE_WORK_ORDER.md`

Also inspect the current implementation before editing:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/
  EnvironmentDiscoveryContracts.cs
  EnvironmentDiscoveryReportGenerator.cs
  EnvironmentDiscoveryReportWriter.cs

src/SteamInputAddonforClaw/Input/DirectInput/
  DirectInputContracts.cs
  DirectInputDeviceTopologyResolver.cs
  VorticeDirectInputDeviceEnumerator.cs

src/SteamInputAddonforClaw/Controllers/Detection/
  WindowsControllerDeviceEnumerator.cs

tests/SteamInputAddonforClaw.Tests/
  EnvironmentDiscoveryReportTests.cs
  VorticeDirectInputDeviceEnumeratorTests.cs
```

The application is pre-release. Do not preserve an obsolete diagnostic schema merely for source compatibility.

---

# 1. Problem statement

A 2026-09-18 hardware capture showed an important mismatch between Windows PnP topology and Steam's SDL-side controller discovery.

## 1.1 Current Windows PnP evidence

`EnvironmentDiscovery-20260918-160519.log` was captured at:

```text
CapturedAt: 2026-09-18T16:05:19.1618173+09:00
AppVersion: 0.1.234.0
```

At that instant the present MSI PID1902 topology contained exactly one physical USB root:

```text
USB\VID_0DB0&PID_1902\5&3AF6909B&0&4
```

with one current gamepad top-level collection:

```text
HID\VID_0DB0&PID_1902&MI_00&COL01\7&F02B9F1&0&0000
UsagePage=0001
Usage=0005
HID_DEVICE_SYSTEM_GAME
Present=True
```

and the expected adjacent collections:

```text
MI_00/COL02 -> Usage FFF0/0040  vendor/control
MI_01/COL01 -> Usage 0001/0006 keyboard
MI_01/COL02 -> Usage 0001/0002 mouse
MI_01/COL03 -> Usage 000C/0001 consumer control
```

There was **not** a second present PID1902 gamepad collection in the all-class SetupAPI inventory.

The same report also showed one present VIIPER Xbox 360 USB/XUSB topology:

```text
USB\VID_045E&PID_028E\296013F
USB\VID_045E&PID_028E&IG_01\...
HID\VID_045E&PID_028E&IG_01\...
```

## 1.2 Steam evidence

During the same investigation, Steam `controller.txt` repeatedly reported two local SDL devices for the physical MSI PID1902 identity:

```text
Local Device Found
  type: 0db0 1902
  path: sdl://3
  Product: Љ

Local Device Found
  type: 0db0 1902
  path: sdl://2
  Product: Љ
```

The current SetupAPI snapshot therefore says:

```text
present physical PID1902 gamepad collections = 1
```

while Steam/SDL discovery says:

```text
local 0DB0:1902 logical devices = 2
```

The report does not currently contain enough Windows input-backend evidence to determine whether the second Steam entry originates from:

- DirectInput;
- Raw Input / HID collection projection;
- GameInput grouping/projection;
- or Steam/SDL's own backend/deduplication layer.

This PR exists only to collect that missing evidence.

---

# 2. Product / diagnostic decision

Environment Discovery remains:

```text
manual
one-shot
read-only
best-effort
developer diagnostic
```

Add a new stable report section:

```text
=== WINDOWS CONTROLLER BACKEND DISCOVERY ===
```

with three subsections:

```text
DirectInput
RawInput
GameInput
```

The report must make it possible to answer:

1. How many `0DB0:1902` controller projections does DirectInput enumerate?
2. What exact DirectInput device path / PnP instance maps to the MSI PID1902 gamepad?
3. How many Raw Input HID projections exist for `0DB0:1902`?
4. Which Raw Input usage page / usage and device path belongs to each projection?
5. Does Raw Input expose only `MI_00/COL01`, or also `MI_00/COL02` as a relevant HID input?
6. How many GameInput controller devices represent `0DB0:1902`?
7. Does GameInput group them under one device/root ID or expose duplicates?
8. Are the VIIPER identities `045E:028E` and `28DE:1205` also singular or duplicated in these backends?
9. If all three Windows backends are singular while Steam still reports two `sdl://` entries, is the duplication therefore downstream in Steam/SDL?

The report must **not** automatically decide the cause.

It records evidence only.

---

# 3. Critical Full1902 boundaries

This PR must not change the Full1902 controller-authority contract.

Current authority remains:

```text
Center M Disabled
-> physical desired PID = PID1902
-> one Addon-owned DirectInput source
-> exact primary PID1902 gamepad collection hidden by HidHide
-> exactly one virtual presentation attached
```

The exact-target rule remains unchanged:

```text
hide the exact currently-owned PID1902 primary gamepad collection
do not broad-hide the whole PID1902 tree
```

Do **not** modify:

- `AddonControllerHidHideBaseline`;
- PID1901/PID1902 mode logic;
- physical ownership;
- DirectInput acquire/recovery;
- VIIPER attach/detach;
- Xbox360/SteamDeck presentation policy;
- Steam/BPM observation;
- suspend/resume behavior;
- PnP recovery;
- rumble routing;
- controller publisher behavior.

This diagnostic PR must not add any new hidden target.

In particular, do not respond to the two Steam `Љ` entries by hiding:

```text
MI_00/COL02
MI_01/*
USB\VID_0DB0&PID_1902 root
any wildcard VID/PID target
```

without separate hardware evidence and a separate work order.

---

# 4. Important HidHide interpretation constraint

Environment Discovery runs inside the Addon Runtime.

The Runtime is intentionally whitelisted by the Addon-owned HidHide baseline.

Therefore:

> A physical PID1902 device being visible to Environment Discovery does **not** prove that Steam or another non-whitelisted process can open it.

Do not add fields such as:

```text
ExternallyVisible=true
HidHideFailed=true
SteamCanOpen=true
```

based on these probes.

The backend report is topology/enumeration evidence.

External-process visibility must continue to be judged from actual Steam logs / external behavior and the existing HidHide readback contract.

---

# 5. Current-code facts to preserve

## 5.1 Environment Discovery already has the correct lifecycle shape

Current `EnvironmentDiscoveryReportGenerator.GenerateAsync()` runs the capture as a one-shot task.

Current `WindowsEnvironmentDiscoverySnapshotSource.Capture()` collects independent sections with the existing `Section(...)` helper:

```text
subquery succeeds -> items
subquery throws   -> section Failure=<exception type>
other sections continue
```

Keep this behavior.

A failure in Raw Input or GameInput must not prevent the report from being generated.

## 5.2 PnP inventory is already authoritative for present topology

Current:

```text
WindowsControllerDeviceEnumerator.EnumeratePresentDevices()
```

uses the existing all-class present-device scan.

Do not add another SetupAPI all-class scan for this work.

The new backend section may correlate its paths with existing PnP identities, but the existing:

```text
=== CONTROLLER / PNP DEVICES ===
```

section remains the authoritative present PnP inventory in the report.

## 5.3 Existing DirectInput enumerator is reusable

Current:

```text
VorticeDirectInputDeviceEnumerator
```

already creates `IDirectInput8` and enumerates:

```text
DeviceType.Gamepad
DeviceEnumerationFlags.AllDevices
```

For the exact MSI `0DB0:1902` candidate it already captures:

- DirectInput instance GUID;
- product GUID;
- product name;
- VID/PID;
- interface path;
- PnP instance ID;
- physical root identity;
- usage page / usage;
- button count;
- axis count;
- topology reason.

Reuse it.

Do not introduce a second DirectInput library.

Do not call `CreateDevice`, `Acquire`, `Poll`, or read live input as part of Environment Discovery.

The existing enumeration/inspection path is enough.

It is acceptable to construct it with:

```csharp
new VorticeDirectInputDeviceEnumerator(IntPtr.Zero)
```

because Environment Discovery only calls `EnumerateGameControllers()`; the window handle is only needed by the acquire path.

Do not change the current production optimization that skips deep topology inspection for unrelated non-MSI gamepads solely for this diagnostic PR.

---

# 6. Report schema

Modify:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryContracts.cs
```

Add one nested snapshot equivalent to:

```text
ControllerBackendDiscoverySnapshot
  DirectInput
  RawInput
  GameInput
```

A minimal acceptable shape is:

```csharp
internal sealed record ControllerBackendDiscoverySnapshot(
    DiscoverySection<DirectInputDeviceDescriptor> DirectInput,
    DiscoverySection<RawInputDeviceDiscoveryInfo> RawInput,
    DiscoverySection<GameInputDeviceDiscoveryInfo> GameInput);
```

Using the existing `DirectInputDeviceDescriptor` directly is preferred over duplicating all of its fields into another DTO.

Add:

```text
ControllerBackends
```

to `EnvironmentDiscoverySnapshot`.

### 6.1 RawInputDeviceDiscoveryInfo

Keep only evidence required for correlation:

```text
DevicePath
PnpInstanceId
DeviceType
VendorId
ProductId
VersionNumber
UsagePage
Usage
```

Optional small fields such as a stable diagnostic reason are acceptable if they are directly useful.

Do not add a generalized Windows-input device model.

### 6.2 GameInputDeviceDiscoveryInfo

Capture enough native GameInput identity to diagnose grouping:

```text
EnumerationOrdinal
VendorId
ProductId
RevisionNumber
UsagePage
Usage
DeviceFamily
SupportedInput
DeviceStatus
ContainerId
DeviceId
DeviceRootId
DisplayName
PnpPath
```

`DeviceId` and `DeviceRootId` should be rendered as stable hex strings.

The ordinal is diagnostic evidence only; it is not a persistent identity.

Do not silently deduplicate callback results before recording them.

If GameInput itself enumerates two records, the report must preserve both.

---

# 7. Snapshot version

Current:

```csharp
EnvironmentDiscoveryReportWriter.SnapshotVersion = 2;
```

This PR changes the stable report schema.

Bump it to:

```csharp
SnapshotVersion = 3;
```

Update focused tests accordingly.

---

# 8. DirectInput capture

Implement a one-shot DirectInput capture using the existing enumerator.

Equivalent flow:

```text
construct VorticeDirectInputDeviceEnumerator(IntPtr.Zero)
-> EnumerateGameControllers()
-> copy descriptors into DiscoverySection
-> dispose enumerator
```

Report every returned game controller.

For `0DB0:1902`, the existing descriptor must make the exact path/PnP mapping visible.

Expected hardware evidence from the current machine should look conceptually like:

```text
VID=0DB0
PID=1902
DevicePath=\\?\HID#VID_0DB0&PID_1902&MI_00&COL01#...
PnpInstanceId=HID\VID_0DB0&PID_1902&MI_00&COL01\...
UsagePage=0001
Usage=0005
TopologyReason=VerifiedMsiPhysicalRoot
```

Do not acquire the device.

Do not modify current DirectInput ownership.

---

# 9. Raw Input capture

Add a diagnostics-local one-shot Raw Input enumerator.

Preferred location:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/WindowsControllerBackendDiscovery.cs
```

or a similarly narrow file in the same folder.

Use the standard Win32 Raw Input APIs directly:

```text
GetRawInputDeviceList
GetRawInputDeviceInfoW(... RIDI_DEVICENAME ...)
GetRawInputDeviceInfoW(... RIDI_DEVICEINFO ...)
```

Required native constants/structures are limited to:

```text
RAWINPUTDEVICELIST
RID_DEVICE_INFO
RID_DEVICE_INFO_HID
RIM_TYPEHID
RIDI_DEVICENAME
RIDI_DEVICEINFO
```

Do not register for live Raw Input.

Do not create a hidden window.

Do not call `RegisterRawInputDevices`.

This is enumeration only.

## 9.1 Raw Input filtering

Do not dump every keyboard/mouse/HID endpoint on the system into an already-large Environment Discovery file.

Retain a Raw Input HID item when either:

```text
UsagePage == 0x0001
AND Usage is Joystick(0x0004) or Gamepad(0x0005)
```

or the VID/PID is one of the controller identities relevant to this product/investigation:

```text
MSI Claw known controller identity under VID 0DB0
VIIPER Xbox360 045E:028E
VIIPER SteamDeck 28DE:1205
```

Reuse current product constants where practical.

Do not create a second authority table for MSI controller identities.

This intentional exception for the exact tracked VID/PIDs is important because it lets the report prove whether a vendor-specific MSI collection such as:

```text
MI_00/COL02
Usage FFF0/0040
```

is also projected through Raw Input.

## 9.2 Raw Input device path / PnP identity

Capture `RIDI_DEVICENAME` exactly.

For HID-style paths such as:

```text
\\?\HID#VID_0DB0&PID_1902&MI_00&COL01#...#{GUID}
```

derive the corresponding PnP instance ID:

```text
HID\VID_0DB0&PID_1902&MI_00&COL01\...
```

A small diagnostics-local parser is acceptable.

Do not create a generalized topology abstraction just to share the current private parser in `DirectInputDeviceTopologyResolver`.

If a small existing helper can be reused without changing production behavior, that is also acceptable.

The important constraint is: no architecture expansion for an 8-10 line path conversion.

---

# 10. GameInput capture

Add a diagnostics-local, one-shot Microsoft GameInput enumeration.

The target machine currently has Microsoft GameInput installed and running, but GameInput availability must still be treated as optional diagnostic evidence.

Do **not** add a new NuGet package solely for this PR.

Specifically, do not add:

```text
Microsoft.GameInput
GameInput.Net
GameInputSharp.Core
```

to the product just to generate a developer report.

Do not ship another GameInput redistributable from this work order.

## 10.1 Native ABI approach

Use the installed Windows `GameInput.dll` through minimal diagnostics-local P/Invoke / COM declarations.

Relevant public API references:

```text
GameInputCreate / IGameInput
IGameInput::RegisterDeviceCallback
IGameInputDevice::GetDeviceInfo
GameInputBlockingEnumeration
GameInputDeviceInfo
```

Microsoft's repository contains a C# interop companion that may be used as an **ABI reference**:

```text
microsoftconnect/GameInput
companion/c#interop/GameInputInteropV3.cs
```

Do not vendor/copy that full file into this repository.

Define only the minimal interop signatures and structs required by this one diagnostic capture.

No GameInput mapper, haptics, force feedback, readings, or production input path belongs in this PR.

## 10.2 Enumeration pattern

Use the API's blocking device enumeration capability rather than a persistent listener.

Conceptually:

```text
GameInputCreate
-> RegisterDeviceCallback(
     device = null,
     inputKind = Controller | Gamepad,
     statusFilter = AnyStatus,
     enumerationKind = BlockingEnumeration,
     callback = local capture callback)
-> blocking initial enumeration completes
-> unregister callback immediately
-> release GameInput COM ownership
```

The callback must:

1. read `IGameInputDevice::GetDeviceInfo`;
2. copy all required fields into a managed immutable diagnostic record while the callback/device is valid;
3. record current device status;
4. return immediately.

Do not keep device COM objects after capture.

Keep the callback delegate rooted until unregistration completes.

A simple local lock or thread-safe collection is sufficient if the GameInput callback thread requires it.

Do not introduce an epoch, worker service, callback manager, or state machine.

## 10.3 GameInput device info

From `GameInputDeviceInfo`, capture only:

```text
vendorId
productId
revisionNumber
usage.page
usage.id
deviceId
deviceRootId
deviceFamily
supportedInput
containerId
displayName
pnpPath
```

Convert the native display-name / PnP-path pointers while valid.

Do not dereference unrelated optional nested info pointers.

If `GetDeviceInfo` fails for one callback, represent that callback as a failed/incomplete diagnostic item if practical, or fail the GameInput subsection cleanly.

Do not crash the whole report.

## 10.4 GameInput failure handling

Any of these must degrade only the GameInput subsection:

```text
DllNotFoundException
EntryPointNotFoundException
COM/interop exception
GameInputCreate HRESULT failure
RegisterDeviceCallback HRESULT failure
GetDeviceInfo HRESULT failure
```

The final Environment Discovery file must still be written.

Where an HRESULT exists, preserve it in a stable hex form if the implementation can do so without expanding the general `DiscoverySection` contract.

A simple failure string such as:

```text
GameInputCreateFailed:0x........
```

is acceptable.

---

# 11. Report writer

Modify:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryReportWriter.cs
```

Insert the new section immediately after:

```text
=== CONTROLLER / PNP DEVICES ===
```

and before:

```text
=== WINDOWS MOTION / SENSOR DISCOVERY ===
```

Required shape:

```text
=== WINDOWS CONTROLLER BACKEND DISCOVERY ===

DirectInput:
CandidateCount=N
...

RawInput:
CandidateCount=N
...

GameInput:
CandidateCount=N
...
```

If a backend fails:

```text
DirectInput:
<InspectionFailed: ...>
```

and the writer must continue with the remaining backends.

## 11.1 DirectInput output

For every descriptor include:

```text
InstanceGuid
ProductGuid
ProductName
VID
PID
DevicePath
PnpInstanceId
PhysicalIdentity
UsagePage
Usage
ButtonCount
AxisCount
TopologyReason
```

Use existing `Safe(...)` and hex-formatting conventions.

## 11.2 Raw Input output

For every retained item include:

```text
DeviceType
DevicePath
PnpInstanceId
VID
PID
VersionNumber
UsagePage
Usage
```

Sort deterministically by:

```text
VID
PID
UsagePage
Usage
DevicePath
```

## 11.3 GameInput output

For every callback record include:

```text
EnumerationOrdinal
VID
PID
RevisionNumber
UsagePage
Usage
DeviceFamily
SupportedInput
DeviceStatus
ContainerId
DeviceId
DeviceRootId
DisplayName
PnpPath
```

Sort the rendered output deterministically by identity/path while retaining the ordinal field.

Do not collapse two records merely because VID/PID are equal.

The entire point of the report is to reveal duplicate projections.

---

# 12. Capture composition

Update:

```text
WindowsEnvironmentDiscoverySnapshotSource.Capture()
```

to capture the backend snapshot once.

Conceptually:

```text
var devices = new WindowsControllerDeviceEnumerator();

existing system/environment capture
existing all-class PnP capture
new controller backend capture
existing prerequisite capture
existing motion-sensor capture
```

The three backend captures must remain independently failure-isolated.

Preferred shape:

```text
ControllerBackends = new(
    Section(CaptureDirectInput),
    Section(CaptureRawInput),
    Section(CaptureGameInput))
```

or an equivalent small helper.

Do not make a RawInput failure suppress GameInput.

Do not make GameInput failure suppress DirectInput.

---

# 13. No long-lived behavior

Environment Discovery must return to exactly the same product state after capture.

No new:

- timer;
- polling loop;
- input publisher;
- input reader thread;
- window message hook;
- Raw Input registration;
- GameInput persistent callback;
- DeviceWatcher;
- PnP watcher;
- HidHide mutation;
- VIIPER operation.

After the report is generated there must be no retained backend-discovery object.

---

# 14. Privacy / report hygiene

Preserve the current Environment Discovery privacy rules.

Do not add:

- command-line arguments;
- account names;
- arbitrary registry values;
- controller input values;
- key/button presses.

PnP paths, HID instance IDs, GameInput device IDs/root IDs, VID/PID, and controller display names are acceptable controller diagnostic evidence consistent with the current report.

Do not record actual live button state.

---

# 15. Tests

Modify:

```text
tests/SteamInputAddonforClaw.Tests/EnvironmentDiscoveryReportTests.cs
```

and add narrowly focused tests/helpers only where useful.

## 15.1 Snapshot version and section order

Verify:

```text
SnapshotVersion: 3
```

and:

```text
CONTROLLER / PNP DEVICES
<
WINDOWS CONTROLLER BACKEND DISCOVERY
<
WINDOWS MOTION / SENSOR DISCOVERY
<
ROUTING PREREQUISITES
```

## 15.2 Writer preserves duplicates

Create two Raw Input records or two GameInput records with the same:

```text
VID=0DB0
PID=1902
```

but distinct paths/device IDs.

Verify both are rendered.

This is mandatory.

The writer must not group or deduplicate them.

## 15.3 Backend partial failure

Create a snapshot where:

```text
DirectInput succeeds
RawInput fails
GameInput succeeds
```

Verify:

- RawInput failure is printed;
- DirectInput items remain;
- GameInput items remain;
- motion/prerequisite sections remain.

Add the inverse failure cases if simple.

## 15.4 Raw Input path conversion

Unit-test the diagnostics-local HID path -> PnP instance conversion with:

```text
\\?\HID#VID_0DB0&PID_1902&MI_00&COL01#7&F02B9F1&0&0000#{...}
```

expecting:

```text
HID\VID_0DB0&PID_1902&MI_00&COL01\7&F02B9F1&0&0000
```

Invalid/unexpected paths must return unavailable/null, not throw.

## 15.5 Raw Input relevance filter

Verify the filter retains:

```text
0001/0004 joystick
0001/0005 gamepad
0DB0:1902 vendor usage FFF0/0040
045E:028E
28DE:1205
```

and drops an unrelated vendor-specific HID collection.

## 15.6 DirectInput production behavior remains unchanged

Existing:

```text
VorticeDirectInputDeviceEnumeratorTests
```

must continue to prove:

```text
DeviceType.Gamepad
DeviceEnumerationFlags.AllDevices
```

No test should require a real DirectInput device in CI.

## 15.7 Do not make CI depend on real GameInput hardware

Writer/contract tests should use synthetic GameInput discovery records.

Do not require:

- a controller;
- GameInput service;
- Steam;
- HidHide;
- VIIPER;

for automated tests.

A small pure conversion/helper test is fine.

Do not build a large fake COM framework merely to unit-test one diagnostic callback.

---

# 16. Expected files

Keep the diff concentrated.

Expected modified files:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryContracts.cs
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryReportGenerator.cs
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryReportWriter.cs
tests/SteamInputAddonforClaw.Tests/EnvironmentDiscoveryReportTests.cs
```

Likely new file:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/WindowsControllerBackendDiscovery.cs
```

A second small diagnostics-local interop file is acceptable if it materially improves readability, for example:

```text
GameInputDiscoveryInterop.cs
```

but do not split this into a hierarchy of providers/managers/interfaces.

No project/package change should be necessary.

Avoid unrelated changes to:

```text
HidHide/
VirtualOutput/
Lifecycle/
CenterMStartup/
Overlay/
QAM/
Rumble/
Gyro production path
```

---

# 17. Manual hardware validation

Run on the same supported MSI Claw where Steam shows the two `Љ` entries.

Keep:

```text
Center M Disabled
Addon authority active
physical PID1902
Steam running
current normal virtual presentation
```

Generate a fresh Environment Discovery report while Steam Settings -> Controller still shows the issue.

Save alongside it:

```text
Steam\logs\controller.txt
Steam\logs\controller_ui.txt
current Addon log
```

Do not change HidHide between captures.

## 17.1 Required report checks

First confirm the existing PnP section still shows only one present PID1902 gamepad collection.

Then inspect the new backend section.

Record the counts and exact paths for:

```text
0DB0:1902  physical MSI PID1902
045E:028E  VIIPER Xbox360
28DE:1205  VIIPER SteamDeck
```

For the MSI physical controller, specifically record whether each backend shows:

```text
1 projection
2 projections
more than 2
0
inspection failure
```

## 17.2 Interpretation matrix

Use the evidence as follows.

### Case A

```text
PnP gamepad = 1
DirectInput  = 1
RawInput     = 1
GameInput    = 1
Steam SDL    = 2
```

Conclusion:

```text
No duplicate is demonstrated in these Windows controller backends.
Investigate Steam/SDL HIDAPI/backend deduplication next.
```

Do not change HidHide based on this result.

### Case B

```text
PnP gamepad = 1
DirectInput = 1
RawInput = 2
RawInput paths identify COL01 + COL02
Steam SDL = 2
```

Conclusion:

```text
Raw Input exposes two MSI HID projections.
Compare exact usage/path with Steam behavior before deciding whether
Steam is incorrectly treating the vendor collection as another controller.
```

Still do not broad-hide in this PR.

### Case C

```text
PnP gamepad = 1
GameInput = 2
same VID/PID
different DeviceId/PnpPath or grouping facts
```

Conclusion:

```text
GameInput exposes duplicate logical devices.
Investigate GameInput grouping/device identity separately.
```

### Case D

```text
PnP section itself shows >1 present 0001/0005 PID1902 gamepad collections
```

Conclusion:

```text
The problem is no longer merely Steam logical duplication.
Investigate physical PnP re-enumeration / present topology.
```

This was **not** the state in `EnvironmentDiscovery-20260918-160519.log`.

### Case E

```text
Windows backends are singular
but only the VIIPER 045E:028E identity duplicates in Steam
```

Conclusion:

```text
Treat the historical Xbox360 duplication as a separate Steam/SDL/XUSB
enumeration issue from the PID1902 Љ display issue.
```

---

# 18. Explicit non-goals

Do not implement any of the following in this PR:

- hide the second `Љ` entry;
- modify HidHide targets;
- broad-hide all PID1902 collections;
- remove PnP devices;
- uninstall stale HID instances;
- re-enumerate/power-cycle the controller;
- change PID1901/PID1902 mode;
- change VIIPER lifecycle;
- change SteamDeck/Xbox360 presentation switching;
- parse Steam's `controller.txt` inside the product;
- integrate SDL;
- ship SDL diagnostics;
- add Windows.Gaming.Input as a fourth backend;
- add XInput-slot polling;
- add a background device monitor;
- add a new diagnostic helper process;
- add a generalized input-backend abstraction.

The next work order, if any, must be chosen from the hardware evidence produced by this one.

---

# 19. Overengineering / race policy

This is a one-shot diagnostic capture.

Do not defend against hypothetical instruction-level races between:

- a Raw Input enumeration call and a random PnP callback;
- a GameInput callback and an unrelated Steam event;
- DirectInput enumeration and a theoretical device transition that is not occurring in the test.

Real device removal during the report is allowed to produce:

```text
partial evidence
inspection failure
different counts between sections
```

That is diagnostic data, not a reason to add epochs/barriers/retries.

A short API-required two-call buffer-size pattern is fine.

GameInput's blocking enumeration is fine.

Do not add retry loops merely to force all three APIs to observe an artificial atomic snapshot.

The existing report already represents a best-effort point-in-time environment inspection, not a transactional hardware snapshot.

---

# 20. Build / validation

Required automated validation:

```text
dotnet build SteamInputAddonforClaw.sln -c Debug
dotnet test SteamInputAddonforClaw.sln -c Debug
dotnet build SteamInputAddonforClaw.sln -c Release
```

Use the repository's current normal test/build commands if the solution path differs.

No new warnings.

No package changes unless implementation proves absolutely unavoidable; if that occurs, stop and justify it rather than silently adding a dependency.

---

# 21. Acceptance criteria

This PR is complete when all of the following are true:

1. Environment Discovery remains manual, one-shot, and read-only.
2. Snapshot schema is bumped from version 2 to version 3.
3. A new `WINDOWS CONTROLLER BACKEND DISCOVERY` section exists.
4. DirectInput enumeration reuses the existing Vortice DirectInput path.
5. DirectInput discovery does not acquire/read the controller.
6. Raw Input enumeration uses standard Win32 enumeration only and registers no live input sink.
7. Raw Input output includes exact device paths and usage page/usage for relevant devices.
8. Raw Input preserves multiple projections when they exist.
9. GameInput enumeration uses a blocking one-shot device callback and unregisters immediately.
10. GameInput output includes device/root identity, PnP path, VID/PID, family, supported input, and status.
11. GameInput preserves duplicate records rather than deduplicating them.
12. Failure of any one backend does not fail the full report.
13. Existing PnP and motion-sensor sections remain intact.
14. Current HidHide exact-target policy is unchanged.
15. No new HidHide target is added.
16. No PID mode write occurs.
17. No VIIPER attach/detach occurs.
18. No controller publisher/input authority behavior changes.
19. No new long-lived callback, timer, watcher, or polling loop remains after report generation.
20. No new GameInput/RawInput wrapper package is added.
21. Automated tests cover section order, duplicate preservation, backend partial failure, Raw Input path conversion, and relevance filtering.
22. Debug/Release builds and the full automated test suite pass.
23. One real hardware report can distinguish whether the duplicate `0DB0:1902` projection exists in DirectInput, Raw Input, GameInput, or only in Steam/SDL.

---

# 22. Final implementation principle

Do not try to fix the symptom before proving where it originates.

The current evidence is:

```text
Windows present PnP:
  one PID1902 gamepad

Steam SDL:
  two 0DB0:1902 local devices
```

This PR adds only the missing observation layer:

```text
Present PnP
   +
DirectInput
   +
Raw Input
   +
GameInput
   +
existing external Steam controller log
        ↓
identify the first layer where duplication appears
```

Then make the smallest follow-up fix at that layer.

Do not broaden HidHide or controller authority preemptively.
