# Work Order — SD6A: Extend Environment Discovery with Windows Motion / Sensor Evidence

## Status

Focused developer-diagnostic work order.

This PR extends the existing **Environment Discovery Report** so one report can capture the Windows motion-sensor / Intel ISH evidence needed before production SD6 gyro work.

This is **not** production gyro implementation, not controller-routing work, and not a new sensor authority layer.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     37cdc25ef5e07fd72965958ee2f34c6f9f919dec
```

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/gyro/README.md`
- `docs/gyro/GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md`
- `docs/gyro/DEVELOPER_DISCOVERY_REPORT_GYRO_REVIEW_2026-09-05.md`
- `docs/gyro/Reference Research_CG3EM Gyro Driver Correction_2026-08-15.txt`

The application is pre-release. Do not preserve obsolete diagnostic contract shapes merely for source compatibility.

---

# 1. Product / diagnostic decision implemented by this PR

The Environment Discovery Report remains a **one-shot, read-only environment snapshot**.

Its new responsibility is to answer:

```text
What Windows motion-sensor projections and sensor-stack evidence
exist on this MSI Claw right now?
```

It must capture enough evidence to distinguish:

```text
sensor hardware / PnP topology exists
but WinRT projection is absent

legacy Sensor API broad enumeration works / fails

direct custom-type lookup works / fails

sensor stack is missing / broken / partially projected
```

It must **not**:

- start continuous motion capture;
- set sensor report intervals;
- register long-lived callbacks/event sinks;
- calibrate gyro;
- infer production SD6 support;
- mutate PID1901/PID1902;
- mutate HidHide;
- create/detach VIIPER devices;
- change Full1902 authority or presentation state.

---

# 2. Current-code proof

## 2.1 Current Environment Discovery has no sensor section

Current:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/
  EnvironmentDiscoveryContracts.cs
  EnvironmentDiscoveryReportGenerator.cs
  EnvironmentDiscoveryReportWriter.cs
```

`EnvironmentDiscoverySnapshot` currently contains:

```text
System
Processes
Services
InstalledApplications
AppPackages
StartupRegistrations
ScheduledTasks
Devices
Prerequisites
```

There is no motion/sensor discovery contract.

`EnvironmentDiscoveryReportWriter.SnapshotVersion` is currently:

```csharp
internal const int SnapshotVersion = 1;
```

This PR must bump it because the report schema gains a new stable section.

## 2.2 Existing PnP capture already enumerates all present classes

Do **not** add a second SetupAPI enumerator just for gyro.

Current `WindowsControllerDeviceEnumerator.EnumeratePresentDevices()` already uses:

```text
DIGCF_PRESENT | DIGCF_ALLCLASSES
```

and returns every present PnP node with:

- instance ID;
- container ID;
- parent / ancestor IDs;
- enumerator;
- hardware IDs;
- compatible IDs;
- class / class GUID;
- service;
- VID/PID;
- friendly name;
- usage page / usage where derivable.

Therefore the new motion section should **reuse the already-captured `snapshot.Devices`** and derive a sensor/ISH-focused subset for readability.

Do not duplicate the PnP scan.

## 2.3 Current legacy Sensor API helper has most of the required COM edge

Current:

```text
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeSensorApi.cs
```

already contains:

```text
ISensorManager.GetSensorsByCategory
ISensorManager.GetSensorsByType
ISensorManager.GetSensorByID
candidate metadata reads
custom XYZ data-key support
COM release ownership
```

But current `Discover()` only performs:

```text
GetSensorsByCategory(SENSOR_CATEGORY_ALL)
→ read all candidates
→ ClawSensorDiscovery.Select(...)
```

and failures become thrown exceptions.

For Environment Discovery, broad enumeration and direct-type lookup must be queryable **independently** and the exact HRESULT must be preserved as report evidence.

Do not create a `MotionManager`, `SensorManagerService`, plugin framework, or production abstraction for this.

A small diagnostics-only extension of the existing low-level Sensor API helper is preferred.

---

# 3. Goal

Add this report section:

```text
=== WINDOWS MOTION / SENSOR DISCOVERY ===
```

A single report from repaired-driver CG3EM must answer:

1. Are relevant Sensor / HID Sensor / Intel ISH PnP nodes present?
2. Does WinRT expose `Gyrometer.GetDefault()`?
3. Does WinRT expose `Accelerometer.GetDefault()`?
4. Does legacy `GetSensorsByCategory(SENSOR_CATEGORY_ALL)` succeed?
5. If it fails, what exact HRESULT was returned?
6. Does direct `GetSensorsByType(E83AF229-8640-4D18-A213-E22675EBB2C3)` return anything?
7. What candidate metadata is returned by each successful query?
8. Can broad enumeration fail while a direct type query still succeeds?

The report must still complete when any one of those subqueries fails.

---

# 4. Required contract changes

Modify:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryContracts.cs
```

Add a narrow diagnostic model. Naming may vary slightly, but keep the shape equivalent to:

```text
MotionSensorDiscoverySnapshot
  WinRtGyrometer
  WinRtAccelerometer
  LegacyCategoryAll
  LegacyDirectTypeQueries[]

WinRtSensorDiscoveryInfo
  Backend
  Available
  DeviceId
  MinimumReportIntervalMs
  Failure
  HResult?

LegacySensorQueryInfo
  QueryKind
  QueryGuid
  Succeeded
  HResult?
  Failure
  Candidates[]

LegacySensorCandidateInfo
  FriendlyName
  SensorId
  TypeGuid
  CategoryGuid
  State
  Manufacturer
  Model
  PersistentUniqueId
  DevicePath
  MinimumReportInterval
  HidUsage
  SupportsCustomX
  SupportsCustomY
  SupportsCustomZ
```

Rules:

- Keep failures as data inside the motion snapshot rather than throwing away the whole section.
- Store HRESULT as a numeric value (`int?`) or equivalent typed value and format it as hex in the writer.
- `Unavailable` is acceptable for metadata the active Windows API cannot provide.
- Do not fabricate sensor identity fields from PnP matching.
- Do not define any `Supported=true` production capability flag in this report.

Add the new `MotionSensors` member to `EnvironmentDiscoverySnapshot`.

---

# 5. Reuse / extend the existing legacy Sensor API helper

Modify as needed:

```text
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeSensorApi.cs
```

Do **not** change current Claw Sensor Probe behavior in this PR except where required to preserve compilation/tests.

Add diagnostics-oriented one-shot query methods equivalent to:

```text
EnumerateByCategory(Guid category)
EnumerateByType(Guid type)
```

Each query must:

```text
call the corresponding ISensorManager method
→ preserve the raw HRESULT
→ if success, enumerate every returned sensor
→ read metadata
→ release collection + sensor COM references
→ return a result object
```

Do not make `GetSensorsByType` select a production accelerometer.

Environment Discovery reports **all returned candidates**.

### 5.1 Required diagnostic candidate type

Include one direct query for the externally validated A2VM reference custom accelerometer type:

```text
E83AF229-8640-4D18-A213-E22675EBB2C3
```

Label it in the report as:

```text
A2VM reference custom accelerometer type
```

This GUID is **not** a CG3EM production contract.

Do not reject a CG3EM candidate because its type or device path differs from A2VM.

### 5.2 Metadata extension

Where the legacy Sensor API exposes them safely, include:

- sensor state;
- device path;
- `SupportsDataField` results for the custom XYZ keys;
- existing manufacturer/model/persistent ID/min interval/HID usage metadata.

Use documented Sensor API contracts/property keys. Do not guess raw vtable slots/property IDs.

If a specific optional property is unavailable on the current OS/driver, report `Unavailable`; do not fail the query.

### 5.3 Preserve the historical broken-driver boundary

The repaired diagnostic must be able to represent the former CG3EM failure as:

```text
LegacyCategoryAll.Succeeded=false
LegacyCategoryAll.HResult=0x80070490
```

rather than collapsing it to only:

```text
<InspectionFailed: COMException>
```

---

# 6. Add WinRT one-shot projection probes

Modify:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryReportGenerator.cs
```

The project already targets:

```text
net10.0-windows10.0.26100.0
```

Use the Windows sensor projection available to this target for one-shot metadata discovery.

Probe:

```text
Windows.Devices.Sensors.Gyrometer.GetDefault()
Windows.Devices.Sensors.Accelerometer.GetDefault()
```

Record at minimum:

```text
Available / absent / exception
DeviceId when exposed
MinimumReportInterval
HRESULT / exception type when meaningful
```

Do not:

- attach `ReadingChanged` callbacks;
- change `ReportInterval`;
- wait for live readings;
- retain the WinRT object after snapshot construction.

If compile-time projection support is already available through the target framework, do not add a NuGet package merely for this feature.

Only add a dependency if the compiler proves the target does not expose the required Windows contracts.

---

# 7. Capture composition

Update `WindowsEnvironmentDiscoverySnapshotSource.Capture()` to add the motion snapshot.

The intended shape is:

```text
Capture existing system sections
Capture existing all-class PnP devices once
Capture WinRT gyro metadata
Capture WinRT accel metadata
Capture legacy CategoryAll query
Capture direct custom-type query
Build EnvironmentDiscoverySnapshot
```

Each motion sub-probe must have its own failure boundary.

Example:

```text
WinRT gyro fails
→ WinRT gyro entry contains failure
→ WinRT accel still runs
→ legacy broad query still runs
→ direct type query still runs
→ report still writes
```

Do not wrap the entire motion section in one catch that loses which boundary failed.

---

# 8. Motion-focused PnP output

Do not re-enumerate PnP.

In `EnvironmentDiscoveryReportWriter`, derive a readable subset from the existing `snapshot.Devices` section.

Include nodes that are clearly relevant to motion/sensor topology, for example:

```text
Class == Sensor
OR UsagePage == 0x20 (HID Sensor)
OR known Intel ISH / Integrated Sensor Solution identity appears in
   FriendlyName / InstanceId / HardwareIds / CompatibleIds / Service
```

Important:

- this is **diagnostic display filtering only**;
- it is not sensor-selection authority;
- do not use a fuzzy match from this subset to decide the production gyro/accel source;
- if no subset matches, print an explicit count/empty result rather than omitting the subsection.

The full existing `CONTROLLER / PNP DEVICES` section remains unchanged.

---

# 9. Report writer changes

Modify:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryReportWriter.cs
```

Change:

```csharp
SnapshotVersion = 1
```

to:

```csharp
SnapshotVersion = 2
```

Add the new section after the general PnP device section and before routing prerequisites, unless a nearby ordering is clearly cleaner.

Recommended output shape:

```text
=== WINDOWS MOTION / SENSOR DISCOVERY ===

PnPRelevantCount: N
...

WinRT Gyrometer:
Available=true
DeviceId=...
MinimumReportIntervalMs=10

WinRT Accelerometer:
Available=false
Failure=<Unavailable>

Legacy CategoryAll:
Succeeded=false
HResult=0x80070490
CandidateCount=0

Legacy DirectType:
Label=A2VM reference custom accelerometer type
TypeGuid=E83AF229-8640-4D18-A213-E22675EBB2C3
Succeeded=true
HResult=0x00000000
CandidateCount=1
...
```

Formatting requirements:

- deterministic ordering;
- GUIDs normalized consistently;
- HRESULT emitted as eight-digit uppercase hex;
- preserve existing user-profile path sanitization policy;
- metadata-scale only — no live XYZ rows.

Do not add sensor data to `KEYWORD MATCHES` unless it materially improves the existing report; the dedicated section is sufficient.

---

# 10. Frontend / UI scope

Current generation path:

```text
DeveloperPage
→ IAddonFrontendControl.GenerateEnvironmentReportAsync()
→ InProcessAddonFrontendControl
→ EnvironmentDiscoveryReportGenerator
```

The return contract remains:

```text
FrontendEnvironmentReportResult(bool Succeeded, string? Error)
```

No new frontend transport DTO is required because the report is still written to disk and the UI only needs success/failure.

Expected UI changes:

```text
none
```

Optional text-only change:

The Developer page description may be updated from controller-software-oriented wording to broader system/controller/sensor diagnostic wording if the existing text is now misleading.

Do not redesign the Developer page in this PR.

---

# 11. Privacy / safety requirements

Preserve existing privacy behavior.

Allowed developer diagnostic evidence:

- sensor device IDs / paths;
- PnP instance IDs;
- hardware/compatible IDs;
- sensor GUIDs;
- service/class data;
- driver projection availability;
- HRESULTs.

Do not add:

- command-line arguments;
- user account names;
- arbitrary registry dumps;
- user document paths;
- environment-variable dumps;
- per-sample telemetry.

Every COM/WinRT object opened by this one-shot report must be released/unreferenced before `Capture()` returns.

---

# 12. Tests

Primary test file:

```text
tests/SteamInputAddonforClaw.Tests/EnvironmentDiscoveryReportTests.cs
```

Add focused tests for:

1. `SnapshotVersion` is `2`.
2. Writer emits `WINDOWS MOTION / SENSOR DISCOVERY` in deterministic position.
3. Existing process/service/app/task/device/prerequisite sections retain their ordering/content behavior.
4. WinRT gyro unavailable does not fail the report.
5. WinRT accel unavailable does not fail the report.
6. Legacy CategoryAll failure preserves exact HRESULT.
7. Direct type lookup result is serialized independently of CategoryAll success/failure.
8. Multiple direct-type candidates are all emitted; Environment Discovery does not choose one.
9. Empty candidate lists are explicit and valid.
10. PnP sensor subset is derived from the already-captured device list; no second device scan contract is introduced.
11. Device paths/profile paths remain sanitized where applicable.
12. Optional legacy metadata failure (`State`, `DevicePath`, `SupportsDataField`) degrades to `Unavailable` instead of failing an otherwise successful query.
13. The report still writes when one motion sub-probe fails.

For COM-specific helpers, add unit coverage for pure formatting/result conversion where practical.

Do not create test-only global Sensor API managers or timing state machines merely to mock COM.

---

# 13. Non-goals

Explicitly out of scope:

- live gyro/accelerometer capture;
- CSV output;
- stationary bias measurement;
- axis transform derivation;
- sensor report-interval changes;
- Steam Deck IMU output;
- `MotionState` production type;
- gyro freshness policy;
- gyro calibration;
- synthetic gravity;
- VIIPER changes;
- Full1902 routing changes;
- HidHide changes;
- automatic sensor recovery.

Those belong to the Claw Sensor Probe enhancement and later SD6 production work.

---

# 14. Expected files

Primary modifications:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryContracts.cs
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryReportGenerator.cs
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/EnvironmentDiscoveryReportWriter.cs
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeSensorApi.cs

tests/SteamInputAddonforClaw.Tests/EnvironmentDiscoveryReportTests.cs
```

Optional small text-only modification:

```text
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
```

Do not modify `WindowsControllerDeviceEnumerator` unless current code inspection proves a required motion-PnP field is genuinely unavailable. Its existing all-class enumeration should be reused first.

---

# 15. PR sizing / sequencing

This should be one focused PR.

Target:

```text
roughly <= 500 net new/changed LOC where practical
```

A large generic sensor abstraction is evidence that the PR has drifted out of scope.

Recommended sequence:

```text
1. contracts + one-shot query result shape
2. legacy direct/broad metadata query support
3. WinRT metadata probes
4. writer section + version 2
5. tests
6. optional Developer text polish
```

This PR should merge before the Claw Sensor Probe characterization PR if possible, so the interactive probe can reuse the same low-level legacy query behavior rather than rediscovering it.

---

# 16. Acceptance criteria

Implementation is complete when all of the following are true:

- Environment Discovery still produces a single read-only log file.
- `SnapshotVersion: 2` is emitted.
- A `WINDOWS MOTION / SENSOR DISCOVERY` section exists.
- Relevant Sensor/HID Sensor/ISH PnP evidence is visible without a second PnP enumeration pass.
- WinRT Gyrometer availability is reported.
- WinRT Accelerometer availability is reported.
- legacy CategoryAll success/failure and exact HRESULT are reported.
- direct lookup of `E83AF229-8640-4D18-A213-E22675EBB2C3` is reported as an **A2VM reference query**, not a CG3EM contract.
- direct lookup can succeed even if broad enumeration fails, and both facts remain visible.
- all returned legacy candidates are preserved; no production source is selected.
- optional sensor metadata failures do not destroy the full report.
- no continuous sensor capture is started.
- no controller/HidHide/VIIPER/system mutation is introduced.
- existing Environment Discovery tests pass with the version/section updates.
- full test suite and Release build pass.

Hardware acceptance on repaired CG3EM is **not** a merge blocker for this diagnostics-only PR, but after merge one real report should be collected and preserved for the next SD6A step.
