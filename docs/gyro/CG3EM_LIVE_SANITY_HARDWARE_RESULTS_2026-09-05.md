# CG3EM Live Sanity Hardware Results — 2026-09-05

Date: 2026-09-05  
Device: MSI Claw 8 EX AI+ / CG3EM Launch Pack  
Board: `MS-1T91`  
Addon model: `msi.claw.cg3em`  
App version: `0.1.222.0`  
Probe mode: `LiveSanity`  
Status: **real-hardware evidence; production SD6 source contract not frozen yet**

---

## 1. Purpose

This document records the first two successful real-hardware **Claw Sensor Probe / Live Sanity** captures on CG3EM after the duplicate legacy sensor-selection hotfix implemented by PR #500.

The captures answer four immediate questions:

1. Can the Addon now select and continuously read the real CG3EM gyroscope and accelerometer?
2. Does the accelerometer respond consistently to known physical posture changes?
3. Is the stationary gyroscope zero-rate offset repeatable across materially different postures?
4. Are there practical acquisition stalls or failures that must be considered before production SD6 motion publication?

These captures are diagnostic evidence only. They do **not** yet authorize hard-coded axis transforms, unit declarations, bias constants, smoothing, recovery machinery, or Steam Deck IMU production mapping.

---

## 2. Source captures

### Capture A — approximately 60° upright / stationary

```text
SessionId:
20260905-135508-417-075d3d70a4f34abfa8be12d387ff4f60

StartUtc:
2026-09-05T13:55:08.4232195Z

EndUtc:
2026-09-05T13:55:34.3939144Z

User-described posture:
approximately 60 degrees upright, left stationary
```

Source folder:

```text
C:\GoogleDrive\Addon\Log\ClawSensorProbe\20260905-135508-417-075d3d70a4f34abfa8be12d387ff4f60
```

Files:

```text
claw-sensor-report.json
claw-sensor-live.csv
```

### Capture B — laid on desk / stationary

```text
SessionId:
20260905-141935-958-ae74e60172884bc9bf3220d64ca35c01

StartUtc:
2026-09-05T14:19:35.9593609Z

EndUtc:
2026-09-05T14:20:24.9758226Z

User-described posture:
laid on a desk, stationary
```

The CG3EM chassis protrudes on its underside, so the physical device cannot sit perfectly coplanar with the desk. The accelerometer result below measures this residual chassis tilt directly.

Source folder:

```text
C:\GoogleDrive\Addon\Log\ClawSensorProbe\20260905-141935-958-ae74e60172884bc9bf3220d64ca35c01
```

Files:

```text
claw-sensor-report.json
claw-sensor-live.csv
```

---

## 3. Hardware source selection — PR #500 validated on CG3EM

Both sessions successfully selected the same two physical STMicro LSM6DSO motion sources through the legacy Windows Sensor API path.

### Gyroscope

```text
FriendlyName          = Physical Gyrometer
SensorId              = 00760001-0012-0002-0000-000000000000
Manufacturer          = ST_MICRO
Model                 = LSM6DSO
Backend               = LegacySensorApi
MinimumReportInterval = 10
CustomUsage           = 118
SupportsX/Y/Z         = true / true / true
IsDirectTypeMatch     = true
UnitBasis             = Unknown
```

### Accelerometer

```text
FriendlyName          = Physical Accelerometer
SensorId              = 00730001-0012-0002-0000-000000000000
Manufacturer          = ST_MICRO
Model                 = LSM6DSO
Backend               = LegacySensorApi
MinimumReportInterval = 2
CustomUsage           = 115
SupportsX/Y/Z         = true / true / true
IsDirectTypeMatch     = true
UnitBasis             = Unknown
```

Both sources report the same custom type GUID:

```text
E83AF229-8640-4D18-A213-E22675EBB2C3
```

but have distinct logical `SensorId` values and distinct physical roles.

Both captures also report:

```text
WinRT Gyrometer     = unavailable
WinRT Accelerometer = unavailable
```

and both completed with:

```text
Errors           = []
Warnings         = []
ShutdownTimedOut = false
```

This is the first real CG3EM confirmation that the PR #500 selection correction works through source selection, source opening, continuous data acquisition, report finalization, and clean teardown.

---

## 4. Official report-level cadence and reliability

The values in this section come from the finalized `claw-sensor-report.json` timing and global summary fields.

### Capture A — approximately 60° upright

| Metric | Gyroscope | Accelerometer |
|---|---:|---:|
| Global summary sample count | 2,501 | 6,978 |
| Global summary duration | 25,461.10 ms | 25,538.14 ms |
| Effective rate | 98.19 Hz | 273.20 Hz |
| Average interval | 10.184 ms | 3.660 ms |
| Maximum interval | 120.757 ms | 46.978 ms |
| FreshCount | 2,503 | 6,981 |
| DuplicateCount | 0 | 0 |
| NoDataCount | 0 | 0 |
| ReadFailureCount | 0 | 0 |
| MaxReadDuration | **140.862 ms** | 63.886 ms |
| MaxFreshAge | **140.864 ms** | 63.894 ms |
| LongReadCount (`>=100 ms`) | **6** | 0 |
| DroppedSampleCount | 0 | 0 |

### Capture B — laid on desk

| Metric | Gyroscope | Accelerometer |
|---|---:|---:|
| Global summary sample count | 4,816 | 13,492 |
| Global summary duration | 48,791.83 ms | 48,871.57 ms |
| Effective rate | 98.68 Hz | 276.05 Hz |
| Average interval | 10.133 ms | 3.623 ms |
| Maximum interval | 121.011 ms | 45.536 ms |
| FreshCount | 4,817 | 13,493 |
| DuplicateCount | 0 | 0 |
| NoDataCount | 0 | 0 |
| ReadFailureCount | 0 | 0 |
| MaxReadDuration | **126.389 ms** | 47.460 ms |
| MaxFreshAge | **126.390 ms** | 47.466 ms |
| LongReadCount (`>=100 ms`) | **8** | 0 |
| DroppedSampleCount | 0 | 0 |

### Immediate conclusion

Normal acquisition is healthy:

```text
Gyroscope     ≈ 98.2–98.7 Hz fresh publication
Accelerometer ≈ 273–276 Hz fresh publication

Duplicate      = 0
NoData         = 0
ReadFailure    = 0
Dropped sample = 0
```

The minimum-report-interval metadata must **not** be interpreted as measured production cadence. The real capture establishes the observed rates above.

---

## 5. CSV-derived stationary vector analysis

The values in this section are calculated from accepted fresh rows in `claw-sensor-live.csv`.

They are not fields directly emitted by the report. They are post-capture analysis of the recorded XYZ samples.

### 5.1 Capture A — approximately 60° upright

#### Accelerometer mean

```text
X = -0.008286
Y = +0.847198
Z = +0.547189
```

Mean vector magnitude:

```text
|a| = 1.00858
```

Per-sample magnitude statistics:

```text
mean = 1.00858
std  = 0.00236
min  = 1.00024
max  = 1.01681
```

Using the mean gravity vector, the angle away from the positive Z axis is:

```text
57.14°
```

This closely matches the user-described physical posture of approximately 60°.

#### Gyroscope stationary mean

```text
X = +0.42103
Y = -0.28593
Z = -0.18270
```

Population standard deviation:

```text
X = 0.10702
Y = 0.10867
Z = 0.05525
```

The device was stationary, so the non-zero mean is strong evidence of a repeatable zero-rate offset in the raw CG3EM gyroscope output. The report still declares `UnitBasis = Unknown`, so this document records the raw values without yet freezing a physical-unit contract.

### 5.2 Capture B — laid on desk

#### Accelerometer mean

```text
X = -0.008833
Y = -0.097227
Z = +1.005336
```

Mean vector magnitude:

```text
|a| = 1.01006
```

Per-sample magnitude statistics:

```text
mean = 1.01007
std  = 0.00268
min  = 0.99143
max  = 1.02028
```

Using the mean gravity vector, the angle away from the positive Z axis is:

```text
5.55°
```

This is consistent with the physical chassis constraint reported by the user: the device was laid on the desk, but the underside protrusion prevents a mathematically perfect flat orientation.

#### Gyroscope stationary mean

```text
X = +0.40657
Y = -0.28968
Z = -0.18309
```

Population standard deviation:

```text
X = 0.12043
Y = 0.15640
Z = 0.04982
```

---

## 6. Posture comparison

The two stationary captures materially changed the gravity vector:

```text
Capture A: approximately 57.14° away from +Z
Capture B: approximately  5.55° away from +Z
```

but the gyroscope stationary mean changed only slightly:

| Axis | Capture A | Capture B | B - A |
|---|---:|---:|---:|
| X | +0.42103 | +0.40657 | -0.01446 |
| Y | -0.28593 | -0.28968 | -0.00375 |
| Z | -0.18270 | -0.18309 | -0.00038 |

This is important evidence.

A major posture change did **not** materially move the gyro zero-rate center. At this stage the observed offset is more consistent with a stable device/sensor zero-rate offset than with a gravity/posture-dependent artifact.

A useful provisional raw center for further comparison is therefore approximately:

```text
X ≈ +0.41
Y ≈ -0.29
Z ≈ -0.18
```

This is **not yet a production hard-coded bias constant**. Stationary Bias mode captures are still required before deciding whether production should subtract a fixed measured bias, reacquire bias at runtime, or apply another minimal policy.

---

## 7. Accelerometer unit evidence

The probe currently reports:

```text
UnitBasis = Unknown
```

for the legacy Physical Accelerometer source.

However the two real stationary postures produce:

```text
Capture A mean |a| = 1.00858
Capture B mean |a| = 1.01006
```

and the gravity-vector direction tracks the known physical posture change from approximately 60° upright to nearly flat.

Therefore the current hardware evidence strongly supports the interpretation that the exposed legacy accelerometer values behave as **g-scaled acceleration**.

This should still be treated as a measured inference until the remaining characterization captures are complete. Do not change `UnitBasis` or production scaling solely from these two Live Sanity sessions without reconciling Axis Characterization and Stationary Bias evidence.

---

## 8. Reproduced gyroscope blocking-read behavior

The most important non-failure behavior reproduced across both captures is a recurring long blocking read on the **legacy gyroscope** source.

### Capture A

Gyroscope reads with `read_duration_ms >= 100`:

```text
6 total
```

One occurs near startup. Excluding that first startup-area event, **5 steady-state long reads** remain.

Representative steady-state events:

```text
~120.1 ms
~119.5 ms
~119.9 ms
~120.8 ms
~120.3 ms
```

Maximum:

```text
140.862 ms
```

Several returned samples had approximately:

```text
113–115 ms sensor age
```

at receive time.

### Capture B

Gyroscope reads with `read_duration_ms >= 100`:

```text
8 total
```

One occurs near startup. Excluding that first startup-area event, **7 steady-state long reads** remain.

Representative steady-state events again cluster around:

```text
119–121 ms
```

Maximum:

```text
126.389 ms
```

Several returned samples again had approximately:

```text
114–116 ms sensor age
```

at receive time.

### Accelerometer contrast

Neither capture produced a `>=100 ms` accelerometer long read:

```text
Capture A accel LongReadCount = 0
Capture B accel LongReadCount = 0
```

### Interpretation

This is now a reproduced CG3EM acquisition characteristic rather than a single-capture anomaly:

```text
Gyro normally publishes at ~100 Hz
        ↓
legacy read occasionally blocks for ~120 ms
        ↓
no backend exception / no source loss
        ↓
first returned sample after the block can already be ~115 ms old
        ↓
normal ~100 Hz publication resumes
```

The captures do **not** identify the cause. They only prove the behavior at the Addon's legacy Sensor API read boundary.

Do not infer from these two captures that a new retry state machine, extra motion manager, secondary publisher, buffering framework, or lifecycle authority is required. More evidence is needed before choosing the smallest production policy.

The existing production Steam Deck publisher can continue to own the single publication cadence; later SD6 design must simply decide how stale a motion snapshot may be before publishing neutral/held motion or otherwise failing safely.

---

## 9. What is now established for CG3EM

The following are supported by these two real-hardware captures:

### Established hardware/discovery facts

- CG3EM exposes a real STMicro `LSM6DSO` Physical Gyrometer through the legacy Windows Sensor API.
- CG3EM exposes a real STMicro `LSM6DSO` Physical Accelerometer through the legacy Windows Sensor API.
- The gyroscope and accelerometer have distinct `SensorId` values.
- Both are XYZ-capable.
- Both are returned under the shared custom type GUID already observed during discovery.
- WinRT Gyrometer and WinRT Accelerometer are unavailable on this tested CG3EM configuration.
- PR #500's logical SensorId dedupe / role-aware selection succeeds on the real device.

### Established runtime observations

- Gyro fresh cadence is approximately 100 Hz.
- Accelerometer fresh cadence is approximately 275 Hz in these captures.
- No duplicate/no-data/read-failure condition occurred.
- No accepted samples were reported dropped.
- Accelerometer magnitude remains very close to 1 across two very different stationary orientations.
- Accelerometer vector direction follows the actual physical posture.
- Stationary gyro center is consistently around raw `(+0.41, -0.29, -0.18)` across those two postures.
- Legacy gyroscope reads reproducibly exhibit occasional ~120 ms stalls without source failure.

---

## 10. What is not established yet

Do **not** freeze the following from Live Sanity alone:

```text
CG3EM -> Steam Deck axis/sign transform
gyro physical unit declaration
production accelerometer UnitBasis declaration
fixed production gyro bias constant
runtime bias reacquisition policy
stale gyro threshold / neutral-vs-hold policy
Steam Deck quaternion policy
sensor fusion policy
resume/restart reacquisition contract
```

The Live Sanity data is deliberately insufficient for those choices.

---

## 11. Required next hardware captures

Continue with the existing SD6-A characterization plan.

### 11.1 Stationary Bias — desk posture

Use the same practical desk posture as Capture B:

```text
device laid on desk
underside chassis geometry accepted as-is
hands off
approximately 30 seconds
```

Goal:

- verify the Live Sanity gyro center through the dedicated bias summary;
- measure formal mean/stddev/span;
- preserve accel mean/span/magnitude evidence.

### 11.2 Stationary Bias — materially tilted posture

Use approximately the Capture A posture:

```text
roughly 60 degrees upright
hands off
approximately 30 seconds
```

Goal:

- verify whether the dedicated bias summary reproduces the same gyro center;
- further test posture dependence.

### 11.3 Axis Characterization

Run the existing seven-phase sequence:

```text
REST
ROLL LEFT
ROLL RIGHT
PITCH UP
PITCH DOWN
YAW LEFT
YAW RIGHT
```

Goal:

- derive the real CG3EM axis/sign transform;
- do not copy A2VM transforms by assumption.

### 11.4 Lifecycle capture

After axis/bias characterization:

```text
Runtime/app restart -> new Live Sanity
Sleep/Resume        -> new Live Sanity
```

Goal:

- verify fresh rediscovery/reopen behavior;
- verify that no stale sensor handle is reused across practical lifecycle transitions.

---

## 12. Production SD6 implication at this checkpoint

The acquisition direction is now substantially de-risked:

```text
CG3EM
  ├─ Physical Gyrometer / Legacy Sensor API / ~100 Hz
  └─ Physical Accelerometer / Legacy Sensor API / ~275 Hz
             ↓
        latest MotionState
             ↓
existing CanonicalSteamDeckInputPublisher
             ↓
canonical VIIPER Steam Deck 28DE:1205
```

No new motion publisher or device owner is justified by these results.

The main unresolved production questions are now narrower:

1. exact CG3EM axis/sign transform;
2. final unit/scaling declaration;
3. gyro zero-rate correction policy;
4. how the existing publisher should treat a realistically reproduced ~120 ms stale gyro interval;
5. restart/sleep-resume sensor reacquisition behavior.

Those should be answered from the remaining hardware characterization before SD6 production code is written.

---

## 13. Checkpoint conclusion

The two Live Sanity captures are successful and mutually consistent.

The strongest conclusions are:

```text
1. Real CG3EM gyro + accel acquisition works continuously after PR #500.
2. Accelerometer direction tracks physical posture and magnitude stays ~1.01.
3. Stationary gyro center is highly similar at ~57° and ~5.5° postures.
4. Gyro acquisition is normally ~100 Hz but reproducibly experiences isolated ~120 ms blocking reads.
5. Accelerometer acquisition is faster (~275 Hz here) and did not reproduce the >=100 ms stalls.
6. No read failure, duplicate, no-data, drop, or teardown failure occurred in either capture.
```

This is sufficient to close the initial CG3EM Live Sanity checkpoint and proceed to dedicated Stationary Bias and Axis Characterization captures.