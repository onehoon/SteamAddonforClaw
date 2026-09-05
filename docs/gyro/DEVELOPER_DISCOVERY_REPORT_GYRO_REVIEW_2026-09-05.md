# Developer Environment Discovery Report — Gyro / Sensor Discovery Review

Date: 2026-09-05  
Scope: Developer Menu `Environment Discovery Report`  
Status: review/design; separate from Claw Sensor Probe implementation

---

## 1. Conclusion

**Enhancement is needed before SD6.**

The current Environment Discovery Report is useful for controller/software/runtime diagnosis, but it cannot answer the key question left by the CG3EM gyro investigation:

> What Windows sensor/ISH topology and motion-sensor projections are actually present on this machine right now?

Current report version 1 captures:

- OS/system/device identity;
- running processes;
- Windows services;
- installed applications and AppX packages;
- startup registrations;
- scheduled tasks;
- controller/PnP devices through the controller enumerator;
- HidHide / usbip-win2 / VIIPER prerequisite assessment;
- keyword matches.

It does **not** capture Windows Sensor API inventory, direct sensor-type lookup results, WinRT gyro/accelerometer availability, Intel ISH-specific sensor evidence, sensor HRESULT/state, or sensor device paths.

That omission matters because the 2026-08-12 CG3EM failure was specifically a **sensor inventory / driver-state** problem, and the later driver reinstall changed the conclusion without changing the controller architecture.

---

## 2. Current implementation reviewed

Primary files:

```text
src/SteamInputAddonforClaw/Diagnostics/EnvironmentDiscovery/
  EnvironmentDiscoveryContracts.cs
  EnvironmentDiscoveryReportGenerator.cs
  EnvironmentDiscoveryReportWriter.cs

src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml(.cs)
```

Current `EnvironmentDiscoverySnapshot` has no motion/sensor section. `WindowsEnvironmentDiscoverySnapshotSource.Capture()` gathers broad system state and the report writer emits `SnapshotVersion = 1`.

This feature should remain **passive and one-shot**. It is an environment report, not a live sensor test.

---

## 3. Required new section

Add a dedicated section such as:

```text
=== WINDOWS MOTION / SENSOR DISCOVERY ===
```

The section should be diagnostic evidence only. It must not decide production gyro support or mutate controller/sensor configuration.

### 3.1 PnP / Intel ISH evidence

Capture relevant present devices, including enough information to identify the sensor stack:

- Sensor-class devices;
- Intel Integrated Sensor Solution / ISH nodes;
- relevant HID Sensor Collection nodes where exposed;
- instance ID;
- parent/container identity where the existing enumerator can provide it;
- class/class GUID;
- service;
- hardware/compatible IDs;
- present status.

Do not broaden the normal controller enumerator into a universal PnP framework just for this. A small diagnostic-only sensor/PnP query is enough if the current controller enumerator filters away the needed classes.

### 3.2 WinRT projection inventory

Report availability and metadata without starting a long-running capture:

```text
Gyrometer.GetDefault()
Accelerometer.GetDefault()
```

Where available, record at minimum:

- present / absent / exception;
- minimum report interval;
- device identity metadata that the Windows projection exposes and is safe to log.

Do **not** subscribe to continuous readings in Environment Discovery.

### 3.3 Legacy Sensor API broad enumeration

Run the equivalent of the current ClawSensorProbe broad discovery and preserve its outcome explicitly:

```text
ISensorManager.GetSensorsByCategory(SENSOR_CATEGORY_ALL)
```

Record:

- HRESULT/result;
- candidate count;
- each candidate's friendly name;
- sensor ID;
- type GUID;
- category GUID;
- state;
- manufacturer/model;
- persistent unique ID where available;
- device path where available;
- minimum report interval;
- HID usage where available;
- supported custom XYZ data fields where relevant.

The 2026-08-12 `0x80070490` result must appear as evidence in the report, not collapse into a generic `COMException` or a missing section.

### 3.4 Direct sensor-type lookup

This is the most important addition from the WSGM comparison.

The report should be capable of issuing a **read-only direct**:

```text
ISensorManager.GetSensorsByType(typeGuid)
```

for diagnostic candidate types.

For A2VM reference research, the known physical accelerometer type is:

```text
E83AF229-8640-4D18-A213-E22675EBB2C3
```

This GUID must be labeled **A2VM reference / diagnostic candidate**, not a CG3EM production contract.

The purpose is to answer:

- does CG3EM expose the same custom type?;
- does direct type lookup succeed even when broad inventory behaves differently?;
- what exact sensor identity is returned?;

If future CG3EM capture discovers a different type, the report can add that model-specific diagnostic candidate explicitly. Do not create a generalized registry of arbitrary sensor GUIDs before there is evidence for more than the supported Claw models.

---

## 4. Data contract recommendation

Bump the report schema/snapshot version when the section is added.

A narrow shape is enough, conceptually:

```text
MotionSensorDiscoverySnapshot
  PnpSensors
  WinRtGyrometer
  WinRtAccelerometer
  LegacyCategoryEnumeration
  LegacyDirectTypeLookups[]
```

Each legacy lookup should preserve both successful results and the exact failure/HRESULT.

Suggested candidate fields:

```text
Backend
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
SupportsCustomX/Y/Z
```

No live X/Y/Z sample belongs in Environment Discovery. That remains the Claw Sensor Probe's job.

---

## 5. Code reuse / ownership

The current `ClawSensorProbeSensorApi` already has much of the legacy COM machinery, including an `ISensorManager.GetSensorsByType` declaration, but its broad discovery is tightly shaped around the interactive probe.

The preferred cleanup is **not** to introduce a `SensorManager`, `MotionManager`, plugin framework, or new authority layer.

Use one small diagnostics-level legacy Sensor API reader that can support both:

- one-shot metadata discovery for Environment Discovery;
- live/report access for Claw Sensor Probe.

If extracting that common low-level COM edge would enlarge the change substantially, duplication of a few P/Invoke/COM definitions is preferable to introducing a broad abstraction. The owner should still be obvious and diagnostics-only.

Production SD6 code should not depend on the Environment Discovery report generator.

---

## 6. Read-only / safety rules

Environment Discovery must remain safe to run while the Addon runtime is active.

It must not:

- call sensor `SetProperties`;
- request permissions interactively;
- set event sinks;
- set report intervals;
- keep sensor handles alive after the snapshot;
- issue controller firmware motion commands;
- switch PID1901/PID1902;
- touch HidHide ownership beyond the existing prerequisite inspection;
- create/detach VIIPER devices;
- start a high-frequency polling loop.

Every COM/WinRT object opened for the report should be released before `Capture()` returns.

---

## 7. Failure reporting

The report is most useful when driver state is broken, so sensor discovery failures must be first-class output.

Examples:

```text
LegacyCategoryEnumeration.HResult=0x80070490
LegacyDirectTypeLookup[E83AF229-...].HResult=...
WinRT.Gyrometer=Unavailable
WinRT.Accelerometer=Unavailable
SensorPnP.Count=0
IntelISH.Present=true
```

Avoid turning all of those into a single:

```text
<InspectionFailed: COMException>
```

The exact boundary is what differentiates:

- absent hardware;
- broken/missing driver projection;
- custom sensor hidden from WinRT;
- permission/state issue;
- wrong identity assumption.

---

## 8. Privacy / report size

Keep the existing path sanitization behavior.

Sensor device paths, PnP instance IDs, GUIDs and hardware IDs are appropriate diagnostic data for this developer-only report. Do not add user documents, account names, arbitrary registry dumps, or full driver binary inventories.

The new section should be metadata-scale, not per-sample logging.

---

## 9. Tests needed when implemented

At minimum:

- Snapshot version increments and writer emits the new section.
- No sensor candidates: report still completes with explicit unavailable/failure facts.
- Broad Sensor API failure is preserved with HRESULT.
- Direct type lookup success is serialized independently of broad enumeration result.
- Multiple direct-type candidates are all reported; Environment Discovery does not choose one as production authority.
- WinRT gyro/accel absence does not fail the whole environment report.
- Device paths are sanitized consistently where needed.
- COM objects are released after one-shot capture.
- Existing process/service/controller/prerequisite report sections remain unchanged.

---

## 10. Acceptance target for SD6-A

A single Environment Discovery report from the repaired CG3EM system should let us answer, without another ad-hoc PowerShell probe:

1. Is Intel ISH present?
2. Are Sensor-class/HID sensor nodes present?
3. Does WinRT expose a gyrometer?
4. Does WinRT expose an accelerometer?
5. Does legacy `SENSOR_CATEGORY_ALL` enumerate sensors?
6. Does direct lookup of the A2VM reference custom accelerometer type return anything?
7. What are the returned names/type/category/device paths/states/field capabilities?
8. If anything fails, exactly which API failed with what HRESULT?

Once those facts are available, the interactive Gyro Test can focus on live units, cadence, axes, stalls and bias instead of rediscovering the whole Windows environment every time.
