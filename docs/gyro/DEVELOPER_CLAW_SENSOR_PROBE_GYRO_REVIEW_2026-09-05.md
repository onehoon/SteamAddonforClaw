# Developer Claw Sensor Probe / Gyro Test — SD6 Review

Date: 2026-09-05  
Scope: Developer Menu `Claw Sensor Probe`  
Current purpose: read-only MSI Claw gyroscope / accelerometer discovery and interactive motion capture  
Status: **useful foundation, but needs revision before the new CG3EM SD6 characterization**

---

## 1. Conclusion

The existing probe should be kept and evolved rather than replaced.

It already has several good properties:

- Runtime-owned diagnostic session;
- Developer UI only;
- read-only Sensor API access;
- explicit MSI Claw family gate;
- separate background sensor readers;
- sensor timestamp de-duplication;
- live UI snapshot at a much lower refresh rate than sensor sampling;
- CSV + JSON output;
- manual physical phases for rest / roll / pitch / yaw;
- bounded shutdown and report finalization;
- no coupling to production Steam Deck routing.

However, its discovery and capture assumptions reflect the 2026-08-12 investigation. The newer WSGM/A2VM evidence exposes several gaps that would make the next CG3EM capture incomplete or misleading.

---

## 2. Current implementation reviewed

Primary files:

```text
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/
  ClawSensorProbeContracts.cs
  ClawSensorProbeCoordinator.cs
  ClawSensorProbeReaders.cs
  ClawSensorProbeSensorApi.cs
  ClawSensorProbeWorkflow.cs

src/SteamInputAddonforClaw.UI/Views/
  ClawSensorProbePage.xaml
  ClawSensorProbePage.xaml.cs
```

Current phase set:

```text
REST
ROLL_LEFT
ROLL_RIGHT
PITCH_UP
PITCH_DOWN
YAW_LEFT
YAW_RIGHT
```

Current legacy discovery:

```text
GetSensorsByCategory(SENSOR_CATEGORY_ALL)
→ collect candidates
→ select exactly one FriendlyName == "Physical Gyrometer"
→ select exactly one FriendlyName == "Physical Accelerometer"
```

The current COM interface declares `GetSensorsByType`, but `Discover()` does not use it.

Both selected sensors are then opened by sensor ID and read through the same custom data format:

```text
B14C764F-07CF-41E8-9D82-EBE3D0776A6F
PID 7 / 8 / 9
```

The session is valid only when both legacy friendly-name candidates exist.

---

## 3. Why the discovery model needs to change

### 3.1 `GetSensorsByCategory(ALL)` cannot be the only door

The WSGM A2VM physical accelerometer was intentionally discovered/validated as a custom sensor type using `GetSensorsByType`.

The new probe should run both:

```text
broad legacy enumeration
+
direct type lookup for relevant diagnostic candidate types
```

The broad inventory remains valuable evidence and should not be removed.

### 3.2 Friendly name alone is not enough authority

`Physical Accelerometer` / `Physical Gyrometer` are useful labels but are too weak as the only selection criterion for production-quality characterization.

The diagnostic should expose and, when selecting a candidate for capture, validate as many of these as available:

- backend;
- friendly name;
- sensor ID;
- sensor type GUID;
- category GUID;
- state/ready status;
- manufacturer/model;
- persistent ID;
- device path / Intel sensor-stack identity;
- required XYZ field support;
- finite live data.

The **probe** may show multiple candidates. It should not hide evidence just because the current selection rule cannot choose one.

### 3.3 Gyro and accelerometer may use different backends

The next probe must be capable of characterizing a combination such as:

```text
Gyro  = WinRT Gyrometer
Accel = legacy Sensor API custom sensor
```

This is the major architectural change from the current "two legacy sensors" assumption.

Do not force both sensors through one backend merely to keep the diagnostic model symmetrical.

---

## 4. Recommended probe modes

Keep the UI simple, but separate the questions being answered.

### Mode A — Discovery / live sanity

Fast start. Shows:

- all discovered motion candidates;
- selected backend/candidate for gyro and accel;
- live raw XYZ;
- sample count/rate;
- current sensor age/freshness;
- acceleration magnitude `|g|` where units are confirmed as g.

This is enough to answer "do the sensors work now?" after driver reinstall.

### Mode B — Axis characterization

Retain the current seven physical phases:

```text
REST
ROLL_LEFT / RIGHT
PITCH_UP / DOWN
YAW_LEFT / RIGHT
```

Output should make sign/axis derivation explicit rather than relying on manual CSV inspection.

For each phase and each source, record at least:

- sample count;
- mean XYZ;
- min/max/span;
- effective rate;
- start/end timestamps;
- change from REST baseline.

The resulting report should be sufficient to derive the CG3EM physical-to-application transform without copying A2VM mapping.

### Mode C — Stationary gyro bias capture

Add a dedicated stationary capture path because the current quick REST phase is not enough evidence to decide a production zero-rate correction.

Keep it intentionally simple:

- device placed still;
- fixed capture duration chosen for development (for example a short default plus an optional longer run);
- no motion phases;
- explicit per-axis gyro mean/stddev/min/max/span;
- acceleration magnitude/span to show whether the device remained stationary;
- sample/cadence/stall metrics.

For final confidence, run at least two independent sessions and preferably one flat plus one physically tilted. The report should make comparison easy, but the Addon does not need a persistent calibration database at this stage.

---

## 5. Timing diagnostics that should be added

The current CSV stores:

- Addon receive timestamp;
- elapsed time;
- sensor XYZ;
- sample interval;
- sensor timestamp.

That is a good base, but it is not enough to diagnose the WSGM-style blocking/stale-read problem.

Add per accepted sample or per read attempt as appropriate:

```text
read_start_monotonic
read_end_monotonic
read_duration_ms
sensor_timestamp
receive_timestamp
sensor_age_ms
fresh / duplicate / no-data classification
```

Also aggregate:

- duplicate count;
- no-data count;
- read failure count;
- maximum read duration;
- maximum fresh-sample age;
- long-stall count using a diagnostic threshold;
- effective fresh-report rate.

Avoid permanent 100 Hz verbose logging in production. This is a bounded developer capture, so detailed CSV is appropriate only while a probe session is active.

---

## 6. Gyro quiet/stale semantics

The current reader fails after five seconds without a fresh timestamp. That is useful for detecting a dead stream, but WSGM found that some gyro paths suppress unchanged readings when the device is still.

Before treating "no changed timestamp for N seconds" as a failed gyro on CG3EM, the next capture must establish the actual driver behavior.

Recommended diagnostic distinction:

```text
No new report / duplicate report
≠ automatically sensor failure
```

The probe should report the condition and age. The session should only fail when the source is demonstrably unavailable/unreadable or a model-specific validated liveness rule is violated.

Do not change this blindly: first collect CG3EM data. The present five-second timeout can remain as an interim guard if the report clearly records that it was the reason for failure.

---

## 7. Accelerometer-specific checks

For a candidate believed to report in g, show and record:

```text
magnitude = sqrt(x² + y² + z²)
```

At stationary orientations the magnitude should be plausibly near 1 g even though the components move between axes.

This is a strong diagnostic discriminator between:

- a real physical accelerometer;
- wrong custom fields;
- wrong unit interpretation;
- stale/zero/garbage values.

Do not convert units based only on the A2VM result. Record raw values and label the interpreted unit only after CG3EM evidence supports it.

---

## 8. Zero-rate offset report

The probe should explicitly compute a **bias candidate**, not silently calibrate anything.

For stationary gyro data:

```text
RawMeanX/Y/Z
StdDevX/Y/Z
MinX/Y/Z
MaxX/Y/Z
SpanX/Y/Z
Duration
SampleCount
EffectiveHz
```

If repeated sessions show a stable nonzero mean much larger than the noise, the later production SD6 implementation may subtract it.

Do not put the A2VM reference offset `(+0.75, -0.37, -0.14)` into the CG3EM code or probe as an expected value. It belongs only in comparison documentation.

---

## 9. Backend-aware report schema

The current JSON `SchemaVersion = 1` should be bumped when the capture model changes.

Recommended report concepts:

```text
SelectedGyroscope
  Backend
  Identity
  Type/category
  DevicePath
  UnitBasis
  SourceConfig / requested interval if applicable

SelectedAccelerometer
  same class of metadata

Discovery
  broad legacy result + HRESULT
  direct type lookup results + HRESULT
  WinRT availability

Capture
  mode
  phases
  timing metrics
  raw summaries
  stationary bias candidate
  acceleration magnitude summary
```

Keep CSV raw and analysis-friendly. JSON should be the compact decision report.

---

## 10. WinRT gyro implementation in the diagnostic

If CG3EM repaired-driver discovery confirms a correct WinRT `Gyrometer`, the probe should support it directly rather than forcing gyro through legacy COM.

A temporary diagnostic session may need to request a report interval to obtain useful live readings. If so:

- record the previous/requested/effective interval;
- treat it as session-local acquisition configuration, not persistent device policy;
- unregister the callback and release the WinRT object on Stop/Done/page exit/runtime shutdown;
- do not make Environment Discovery do this.

The diagnostic UI refresh should remain around the current low rate; sensor callbacks should only update the runtime snapshot / capture writer.

---

## 11. Lifecycle behavior

The current page correctly closes the Runtime-owned session when leaving the page. Preserve that.

Add/validate the following real lifecycle cases:

### App / Runtime restart

A new test session must rediscover sensors. Never reuse stored COM sensor IDs as guaranteed permanent selection authority.

### Sleep / hibernate / resume

For SD6-A, do not automate Windows suspend. Instead add clear test evidence:

- session before suspend ends or is marked interrupted;
- after resume, a new discovery/capture confirms handles can be reacquired;
- no stale pre-suspend nonzero motion appears as fresh data.

If later production motion is wired into the runtime, use the existing power/reconcile lifecycle; do not build a separate diagnostic power manager.

### Device/driver loss

If a selected sensor disappears:

- stop accepting its stale values;
- preserve the failure in the report;
- finalize output;
- let the user start a new session after the device returns.

The developer probe does not need an auto-recovery state machine.

---

## 12. UI changes

Current UI is adequate functionally but should expose the new evidence without becoming a dashboard.

Recommended fields:

```text
Gyroscope
  backend | name | type | path/identity | fresh age | Hz

Accelerometer
  backend | name | type | path/identity | fresh age | Hz | |g|

Capture mode
  Discovery / Axis / Stationary Bias
```

During Axis mode, keep the current phase instructions and Next/Previous flow.

During Stationary Bias mode, one Start/Stop flow is enough. Avoid adding a complex calibration wizard.

On completion, summary should explicitly show:

- selected backend/identity;
- fresh rate and worst read duration/age;
- stationary gyro mean/noise/span;
- stationary accel magnitude/span;
- dropped diagnostic rows;
- output folder.

---

## 13. Code-structure recommendation

Do not create a new generalized motion subsystem as part of the diagnostic PR.

The smallest sensible structure is:

```text
ClawSensorProbe
  discovery
    WinRT metadata/candidate probe
    legacy Sensor API broad + direct type probe
  readers
    one gyro reader for selected backend
    one accel reader for selected backend
  existing coordinator/session writer/workflow
```

A narrow shared low-level legacy Sensor API helper with Environment Discovery is reasonable. Beyond that, keep the diagnostic self-contained until SD6-B introduces the actual production `MotionState` source.

---

## 14. Tests needed when implemented

Preserve existing lifecycle/UI tests and add focused coverage for:

- broad legacy enumeration + direct type lookup are both represented;
- direct lookup can succeed independently of broad lookup result;
- partial discovery is reportable even when combined capture cannot start;
- gyro/accel can select different backends;
- candidate validation rejects wrong type/path/field support;
- read duration and sensor age calculations;
- duplicate/no-data counters;
- acceleration magnitude calculation;
- stationary bias summary;
- report schema v2 serialization;
- page exit cancels capture and finalizes/releases readers;
- sensor failure finalizes a report with the exact backend/error;
- no new production routing/VIIPER dependency is introduced.

Do not add timing-race machinery beyond realistic page close, runtime disconnect, sensor blocking/failure, and power/device lifecycle paths.

---

## 15. SD6-A acceptance checklist

Before implementing production motion output, run the improved probe on the repaired CG3EM and preserve:

1. Full discovery metadata for WinRT and legacy paths.
2. Direct custom-type lookup result.
3. Live gyro and accelerometer raw values.
4. Units/magnitude evidence.
5. Fresh cadence and read-duration/age evidence.
6. Seven-phase axis-direction capture.
7. At least two stationary gyro captures for offset/noise comparison.
8. App/runtime restart re-discovery.
9. Sleep/resume re-discovery.
10. A final written CG3EM source contract: backend, identity, fields, units, transform, freshness and any required offset correction.

Only after that should SD6-B production `MotionState` code be written.
