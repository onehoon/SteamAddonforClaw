# Work Order — SD6A: Upgrade Claw Sensor Probe for CG3EM Gyro / Accelerometer Characterization

## Status

Developer-only diagnostic enhancement work order.

This work upgrades the existing **Claw Sensor Probe / Gyro Test** so the Addon can produce the physical-source evidence required before production SD6 motion output.

This is **not** production `MotionState`, not Steam Deck IMU publication, and not a controller-routing PR.

Source-code baseline reviewed for this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
source commit reviewed: 37cdc25ef5e07fd72965958ee2f34c6f9f919dec
```

Subsequent docs-only commits may advance `main`; re-check source before implementation and update only where current code has materially changed.

Before implementation, read and treat these as authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/gyro/README.md`
- `docs/gyro/GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md`
- `docs/gyro/DEVELOPER_CLAW_SENSOR_PROBE_GYRO_REVIEW_2026-09-05.md`
- `docs/gyro/DEVELOPER_DISCOVERY_REPORT_GYRO_REVIEW_2026-09-05.md`
- `docs/gyro/SD6A_ENVIRONMENT_DISCOVERY_SENSOR_EVIDENCE_WORK_ORDER.md`
- `docs/gyro/Reference Research_CG3EM Gyro Driver Correction_2026-08-15.txt`

The application is pre-release. Diagnostic contracts may change when needed; do not preserve obsolete shapes solely for compatibility.

---

# 1. Diagnostic decision implemented by this work

The existing Claw Sensor Probe is kept.

It already has the correct ownership boundary:

```text
Developer UI
→ frontend RPC
→ one Runtime-owned ClawSensorProbeCoordinator
→ read-only sensor acquisition
→ bounded session output
```

The enhancement must preserve that architecture while replacing the old assumption:

```text
both gyro + accel are legacy ISensorManager sensors
selected only by FriendlyName
read through one common custom XYZ key
```

with a backend-aware diagnostic model capable of characterizing the real CG3EM sensor stack.

Expected diagnostic capability:

```text
Gyro  = WinRT Gyrometer OR validated legacy Sensor API source
Accel = WinRT Accelerometer OR validated legacy custom Sensor API source
```

The diagnostic may use different backends for gyro and accelerometer.

No new production motion owner is introduced.

---

# 2. Why this change is required

## 2.1 Current discovery is broad-enumeration + friendly-name only

Current `ClawSensorProbeSensorApi.Discover()` performs:

```text
GetSensorsByCategory(SENSOR_CATEGORY_ALL)
→ enumerate candidates
→ ClawSensorDiscovery.Select(...)
```

Current selection requires exactly one:

```text
FriendlyName == "Physical Gyrometer"
FriendlyName == "Physical Accelerometer"
```

The COM interface already declares `GetSensorsByType`, but current discovery does not use it.

New external A2VM evidence shows a physical accelerometer can be exposed as a legacy **custom sensor type** and is best found/validated through direct `GetSensorsByType(...)`, while gyro can be exposed through WinRT `Gyrometer`.

The probe must therefore preserve broad enumeration as evidence but cannot use it as the only discovery door.

## 2.2 Current reader forces both sources through the same legacy path

Current `ClawSensorProbeReaders` owns one `ClawSensorProbeSensorApi` and starts:

```csharp
_workers = [
    RunAsync(discovery.Gyroscope!, "GYRO", writer),
    RunAsync(discovery.Accelerometer!, "ACCEL", writer)
];
```

Each worker:

```text
GetSensorById
→ ClawSensorProbeSensorApi.ReadXYZ
→ custom data GUID B14C... / PID 7,8,9
```

This cannot characterize a WinRT gyro + legacy accelerometer combination.

## 2.3 Current hard 5-second fresh-report timeout may misclassify quiet sensors

Current:

```csharp
FreshReportTimeout = 5 seconds
```

and a repeated/no-new sensor timestamp for five seconds throws `TimeoutException`.

WSGM observed that some physical gyro paths suppress unchanged reports while stationary.

Until CG3EM behavior is characterized, "no new timestamp" must be treated as a **freshness fact**, not automatically as a dead sensor.

## 2.4 Current report lacks read-stall / age evidence

Current CSV contains:

```text
sequence
utc_timestamp
elapsed_ms
capture_mode
phase
phase_pass
sensor
x/y/z
sample_interval_ms
sensor_timestamp
```

It cannot tell whether:

```text
low apparent rate
```

came from:

```text
blocking API call
repeated report
no-data return
sensor timestamp stall
writer pressure
```

This is important because A2VM hardware captured a real ~200 ms accelerometer read stall.

## 2.5 Current JSON only has one REST summary

Current report `SchemaVersion = 1` stores:

```text
RestSummary
GyroscopeSummary
AccelerometerSummary
Phases (mostly sample counts/timestamps)
```

It does not provide per-phase mean/min/max/span needed to derive CG3EM axis mapping, and the existing REST phase is not enough to establish persistent gyro zero-rate offset.

---

# 3. End state

The upgraded diagnostic must support three simple developer capture modes:

```text
Live Sanity
Axis Characterization
Stationary Bias
```

All modes use one Runtime-owned diagnostic session and the existing low-rate UI polling model.

No mode creates production controller/motion state.

### Live Sanity

Purpose:

```text
Does the selected physical gyro/accel source work now?
```

Display / record:

- selected backend and identity;
- raw XYZ;
- effective fresh rate;
- sample age;
- last read duration;
- duplicate/no-data/read-failure counters;
- acceleration magnitude when unit basis is known as g.

### Axis Characterization

Preserve the existing phase order:

```text
REST
ROLL_LEFT
ROLL_RIGHT
PITCH_UP
PITCH_DOWN
YAW_LEFT
YAW_RIGHT
```

Produce per-phase summaries sufficient to derive the physical-to-application transform without copying A2VM axis mapping.

### Stationary Bias

One simple stationary capture.

No phase wizard.

The user starts capture, leaves the handheld still, and stops capture manually after a useful interval.

Report:

- gyro mean/stddev/min/max/span per axis;
- accel mean/min/max/span;
- accel magnitude mean/min/max/span when unit basis is known;
- duration/sample count/rate;
- duplicate/no-data/stall metrics.

Do not persist a calibration profile in this PR.

---

# 4. PR sequencing / size

This feature is larger than the Environment Discovery enhancement.

Preferred split:

```text
PR A — backend-aware discovery/readers/timing/report schema
PR B — capture modes + stationary-bias / per-phase summaries + UI polish
```

Keep each PR roughly <= 500 net new/changed LOC where practical.

If implementation remains comfortably small after reusing existing code, a single PR is acceptable, but do not force unrelated concerns into one huge diff merely to avoid a second PR.

### PR A mergeable state

PR A is mergeable when:

- current Axis flow still works structurally;
- backend-aware discovery exists;
- WinRT gyro / legacy accel combination is representable;
- timing/freshness metrics are captured;
- schema v2 output exists;
- current frontend lifecycle remains correct;
- no production motion code exists.

### PR B mergeable state

PR B adds:

- explicit Live / Axis / Bias mode selection;
- bias summary;
- per-phase axis summaries;
- final UI evidence presentation.

---

# 5. Backend-aware discovery model

## 5.1 Reuse Environment Discovery low-level work when present

If `SD6A_ENVIRONMENT_DISCOVERY_SENSOR_EVIDENCE_WORK_ORDER.md` has already been implemented, reuse its low-level legacy query support.

Do not create a second COM Sensor API wrapper with the same responsibility.

If it has not yet landed, implement the minimal shared diagnostics-level query support once and make both diagnostics use it.

Do not introduce:

- `MotionManager`;
- `SensorAuthorityManager`;
- `GyroManager`;
- plugin registry;
- generic backend DI framework;
- production sensor service.

This is still developer diagnostics.

## 5.2 Discovery evidence to collect

Preserve:

```text
legacy CategoryAll query
```

Add:

```text
legacy direct type query candidates
WinRT Gyrometer candidate
WinRT Accelerometer candidate
```

Required A2VM reference direct query:

```text
E83AF229-8640-4D18-A213-E22675EBB2C3
```

Label it as external A2VM reference evidence only.

Do not require CG3EM to match:

```text
VID_8087&PID_0AC2
A2VM axis transform
A2VM zero-rate offset
A2VM freshness thresholds
```

## 5.3 Candidate model

Extend the existing candidate shape so the report can distinguish backend and identity.

Equivalent required fields:

```text
Backend              // LegacySensorApi | WinRtGyrometer | WinRtAccelerometer
RoleHint             // Gyro | Accelerometer | Unknown
FriendlyName
SensorId / DeviceId
TypeGuid
CategoryGuid
State
Manufacturer
Model
PersistentUniqueId
DevicePath
MinimumReportIntervalMs
HidUsage
SupportsX/Y/Z
UnitBasis             // Unknown | DegreesPerSecond | G | other proven basis
SelectionReason
```

Not every backend exposes every field. Use `Unavailable` / nullable values; do not fabricate symmetry.

## 5.4 Selection policy for this diagnostic

Selection is diagnostic, model-bounded, and conservative.

### Gyroscope

Preferred diagnostic order:

```text
1. WinRT Gyrometer when present and a finite live reading can be obtained
2. otherwise a unique validated legacy gyroscope candidate
3. otherwise unresolved
```

### Accelerometer

Preferred diagnostic order:

```text
1. WinRT Accelerometer when present and a finite live reading can be obtained
2. otherwise a unique validated direct custom-type candidate
3. otherwise a unique validated broad-enumeration Physical Accelerometer candidate
4. otherwise unresolved
```

Validation should use available facts such as:

- role-appropriate backend;
- finite live values;
- exact type when a direct-type query is used;
- required XYZ field support for legacy custom sensors;
- ready/usable state where exposed;
- unambiguous single candidate.

Do not select purely because friendly name contains a keyword when multiple candidates exist.

If discovery is ambiguous:

```text
report all candidates
→ SelectedGyroscope/SelectedAccelerometer remains null
→ capture start fails cleanly or remains discovery-only
→ finalized report preserves the evidence
```

---

# 6. Reader structure

Current `ClawSensorProbeReaders` assumes two legacy sensor IDs.

Refactor only enough to support one reader per selected source backend.

A small shape is sufficient, conceptually:

```text
selected gyro source
  → WinRtGyroReader OR LegacySensorReader

selected accel source
  → WinRtAccelReader OR LegacySensorReader
```

Do not create a generalized runtime provider hierarchy with factories/registries unless the concrete implementation proves two small reader classes are insufficient.

The coordinator remains the session owner.

## 6.1 WinRT gyro reader

When selected:

- use `Windows.Devices.Sensors.Gyrometer`;
- record/request a useful report interval only for the lifetime of this diagnostic session;
- record minimum/requested/effective interval where observable;
- convert/label units only according to WinRT contract (`deg/s` for Gyrometer);
- unregister callback / release references on stop, close, runtime shutdown, or failure;
- callbacks update the Runtime snapshot/writer only — never UI directly.

The current UI refresh remains ~200 ms.

Do not raise UI refresh to sensor cadence.

## 6.2 WinRT accelerometer reader

Only implement if `Accelerometer.GetDefault()` returns a usable source on the test system or the code path is straightforward under the current Windows target.

When used:

- preserve raw values;
- label unit according to the WinRT contract;
- capture timestamps/freshness the same way as gyro;
- unregister on teardown.

If unavailable, direct legacy custom accelerometer remains the expected fallback path.

## 6.3 Legacy reader

Retain the current Sensor API read path for validated legacy sources.

Add read-attempt timing around `ReadXYZ`:

```text
readStartMonotonic
→ API read
→ readEndMonotonic
→ readDurationMs
```

Classify each attempt as:

```text
Fresh
Duplicate
NoData
Failure
```

Do not emit one CSV row for every duplicate/no-data poll.

Aggregate those counts and keep the raw CSV focused on accepted fresh samples.

This prevents a stationary 100 Hz diagnostic from becoming an unnecessarily huge permanent log.

---

# 7. Freshness / stale behavior

Remove the current assumption:

```text
no fresh timestamp for 5 seconds
→ sensor failure
```

as the general terminal rule for the characterization probe.

New diagnostic policy:

```text
new fresh sample
→ update values + fresh timestamp

no-data / duplicate / quiet source
→ keep last raw value for display only
→ FreshAgeMs keeps increasing
→ mark source stale when appropriate
→ do not silently call it fresh

API exception / handle loss / backend failure
→ record exact backend error
→ fail/finalize the session
```

Important:

- do not publish stale values into any production controller path; there is no production path in this PR;
- do not synthesize zero gyro or gravity merely to make the diagnostic look healthy;
- show the evidence as measured.

If a bounded stale-warning threshold is useful in UI/report, keep it diagnostic-only and label it accordingly. It must not become the production SD6 freshness contract.

---

# 8. Timing metrics

Extend sample/live/report metrics with:

```text
LastReadDurationMs
MaxReadDurationMs
FreshAgeMs
MaxFreshAgeMs
FreshCount
DuplicateCount
NoDataCount
ReadFailureCount
LongReadCount
```

Recommended diagnostic long-read threshold:

```text
100 ms
```

This value is only for highlighting suspicious blocking during capture. It is **not** a production freshness deadline.

For accepted fresh CSV rows add, at minimum:

```text
backend
read_duration_ms
sensor_age_ms
```

Keep existing:

```text
utc timestamp
elapsed monotonic time
phase/mode
sensor raw XYZ
fresh interval
sensor timestamp
```

Where a backend does not expose a meaningful sensor timestamp, leave the field empty and preserve receive/monotonic timing.

---

# 9. Acceleration magnitude

For a source whose unit basis is proven to be `g`, compute:

```text
MagnitudeG = sqrt(x*x + y*y + z*z)
```

Expose it in:

- live snapshot/UI;
- Stationary Bias summary;
- REST/phase summaries where useful.

Do not compute/display `|g|` as if meaningful when unit basis is unknown.

Do not infer `g` solely because A2VM used the same custom data key.

---

# 10. Report schema v2

Current `ClawSensorProbeSessionWriter` emits:

```text
SchemaVersion = 1
Backend = "Windows Sensor API / ISensorManager"
```

Change to schema version 2.

Remove the misleading single-backend field.

Required top-level concepts:

```text
SchemaVersion = 2
SessionId
AppVersion
StartUtc / EndUtc
Device
ResolvedHardware
CaptureMode
Discovery
SelectedGyroscope
SelectedAccelerometer
SourceConfiguration
Phases
PerPhaseSummaries
StationaryBiasSummary
GyroscopeSummary
AccelerometerSummary
TimingSummary
DroppedSampleCount...
Errors
Warnings
```

### Discovery

Preserve:

```text
legacy broad query result + HRESULT
legacy direct query results + HRESULT
WinRT availability
all candidates
selection errors
```

### Selected source

For each source report:

```text
backend
identity
unit basis
requested/effective report interval where relevant
selection reason
```

### Timing summary

At least per source:

```text
FreshCount
DuplicateCount
NoDataCount
ReadFailureCount
AverageFreshIntervalMs
EffectiveFreshHz
MaxReadDurationMs
MaxFreshAgeMs
LongReadCount
```

---

# 11. Axis Characterization summaries

Keep existing seven phases and manual Next/Previous behavior.

Do not add sensor fusion or transform calculation to production code.

For each phase and each sensor source, JSON must include:

```text
SampleCount
MeanX/Y/Z
MinX/Y/Z
MaxX/Y/Z
SpanX/Y/Z
EffectiveHz
StartElapsedMs
EndElapsedMs
```

For accelerometer in known-g units also include magnitude summary.

The report should make it easy for a developer to compare:

```text
REST vs RollLeft / RollRight
REST vs PitchUp / PitchDown
REST vs YawLeft / YawRight
```

but do not automatically hard-code a CG3EM transform from one run.

The final CG3EM transform is a later evidence decision.

---

# 12. Stationary Bias mode

Add a diagnostic capture mode separate from the seven-phase axis workflow.

Required behavior:

```text
select Stationary Bias
→ Start
→ discover/select/read sources
→ record continuously while user keeps handheld still
→ Stop
→ finalize report
```

No countdown/Next/Previous state machine is needed for this mode.

Report per gyro axis:

```text
Mean
StandardDeviation
Min
Max
Span
SampleCount
Duration
EffectiveHz
```

Report accelerometer stability:

```text
MeanX/Y/Z
Min/Max/Span
Magnitude mean/min/max/span when unit is g
```

This is a **bias candidate only**.

Do not:

- subtract the result from live readings;
- persist it;
- write it into settings;
- compare it against A2VM expected values;
- decide production calibration policy automatically.

The purpose is to collect at least two independent CG3EM stationary sessions for later analysis.

---

# 13. Capture mode frontend contract

Current frontend contract has no capture mode and `StartClawSensorProbeAsync()` takes no request.

Add a small explicit enum:

```text
FrontendClawSensorProbeMode
  LiveSanity
  AxisCharacterization
  StationaryBias
```

The selected mode must be carried in the Start request rather than stored as a second UI-side authority.

Preferred contract:

```csharp
Task<FrontendClawSensorProbeSnapshot> StartClawSensorProbeAsync(
    FrontendClawSensorProbeMode mode,
    CancellationToken cancellationToken = default);
```

Update the named-pipe request payload accordingly.

Do not add separate Start RPC methods for every mode.

Expected transport files:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
```

Preserve the current one-session owner and operation-gate behavior.

---

# 14. Frontend snapshot extensions

Current snapshot exposes:

```text
Discovery
Gyro X/Y/Z/Hz/Count
Accel X/Y/Z/Hz/Count
summary counts
output/errors/device identity
```

Extend narrowly.

Recommended additions:

```text
Mode

Candidate.Backend
Candidate.State
Candidate.DevicePath
Candidate.UnitBasis
Candidate.SelectionReason

AxisSnapshot.FreshAgeMs
AxisSnapshot.LastReadDurationMs
AxisSnapshot.IsFresh
AxisSnapshot.Magnitude?   // accel only when meaningful

TimingSummary per source
```

Avoid sending full CSV history through named pipe.

UI polling still receives only compact latest state/summary values.

---

# 15. UI changes

Files:

```text
src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml
src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml.cs
```

Keep the page utilitarian.

Add one capture-mode selector above Start:

```text
Live Sanity
Axis Characterization
Stationary Bias
```

Default:

```text
Live Sanity
```

### Live Sanity UI

Show:

```text
Gyro: backend | identity | XYZ | Hz | age | read duration
Accel: backend | identity | XYZ | Hz | age | read duration | |g| when valid
```

Next/Previous are hidden or disabled.

Stop finalizes the report.

### Axis Characterization UI

Reuse current:

```text
3-second countdown
phase instructions
Previous
Next / Finish Test
```

Do not redesign the workflow.

### Stationary Bias UI

Show simple instruction:

```text
Place the device still on a stable surface.
Leave it untouched during the capture, then press Stop.
```

No Next/Previous.

Show elapsed time and live freshness/rate.

### Completion summary

Show compact evidence:

- selected gyro backend/identity;
- selected accel backend/identity;
- effective fresh rates;
- worst read duration / fresh age;
- duplicate/no-data counts;
- bias mean/stddev/span when Bias mode;
- output directory.

Do not turn this page into a general telemetry dashboard.

---

# 16. Coordinator / workflow rules

Current coordinator has correct important properties:

- one lifecycle gate;
- one navigation gate;
- runtime-owned session;
- terminal failure finalization is non-cancellable;
- bounded reader shutdown;
- page/process shutdown cleanup.

Preserve them.

Do not add a second coordinator per backend.

### Axis mode

Reuse `ClawSensorProbeWorkflow` phases.

### Live / Bias modes

Do not force fake phase transitions through the seven-phase workflow.

The smallest change is acceptable, for example:

- coordinator knows selected `ClawSensorProbeMode`;
- Axis mode uses existing workflow;
- Live/Bias mode enters one recording state without navigation.

Do not build a generic hierarchical state machine.

---

# 17. Lifecycle / teardown

Preserve current UI behavior:

```text
leaving page
→ stop UI polling
→ cancel in-flight page operations
→ CloseClawSensorProbeAsync
→ Runtime stops/finalizes/disposes session
```

Preserve current Runtime shutdown barrier and one-session ownership.

### App / Runtime restart

Every new session re-discovers sensors.

Do not persist COM sensor IDs or WinRT object identity as authority across Runtime restarts.

### Sleep / hibernate / resume

This diagnostic PR does not need an auto-recovery state machine.

Expected behavior:

```text
active capture interrupted by power/device loss
→ stop accepting stale samples
→ preserve error/interruption in report
→ finalize/close session

after resume
→ user starts a new session
→ discovery runs again
```

Do not add a diagnostic `PowerTransitionManager`.

The real production SD6 lifecycle will later use the existing Runtime power/reconcile architecture.

### Sensor/driver loss

Actual backend read/callback failure is terminal for the active session:

```text
record backend + error
→ finalize report
→ release handles/callbacks
→ allow a new session after device/driver returns
```

No automatic retry loop is required.

---

# 18. Full1902 isolation

This work must not change:

- Center M Enabled/Disabled authority;
- desired PID1901/PID1902 state;
- DirectInput ownership;
- HidHide baseline;
- VIIPER ownership;
- X360 vs Steam Deck presentation switching;
- current `CanonicalSteamDeckInputPublisher` cadence;
- Steam/BPM detection;
- rumble/haptics.

The probe is allowed to run because it is read-only diagnostic code gated on MSI Claw family, not because it owns the controller.

Do not connect the probe output to `SteamDeckDeviceStateMapper` or any virtual output.

---

# 19. Tests — backend / writer

Primary existing test file:

```text
tests/SteamInputAddonforClaw.Tests/ClawSensorProbe/ClawSensorProbeTests.cs
```

Preserve existing tests and add focused coverage for:

1. broad legacy query evidence is preserved.
2. direct type lookup evidence is preserved independently.
3. direct type lookup success does not require broad query success.
4. candidate selection does not rely on FriendlyName alone when ambiguous.
5. gyro and accelerometer can select different backends.
6. partial discovery is reportable even if combined capture cannot start.
7. legacy `Fresh` / `Duplicate` / `NoData` classification.
8. duplicate/no-data no longer automatically trips a generic five-second terminal timeout.
9. API exception still becomes a terminal reader error.
10. read duration calculation.
11. fresh age calculation.
12. long-read counter.
13. effective fresh rate calculation.
14. acceleration magnitude only when unit basis is g.
15. schema version 2 output.
16. source backend/identity serialized.
17. per-phase mean/min/max/span.
18. stationary gyro mean/stddev/min/max/span.
19. stationary accel magnitude stability summary.
20. CSV contains backend/read_duration/sensor_age for fresh rows.
21. duplicate/no-data polling does not create one CSV row per attempt.
22. Stop/Fail still finalize the report.
23. bounded reader teardown remains intact.

Tests should use pure/testable calculations and fake reader result records where practical.

Do not create timing-race tests for improbable instruction-level interleavings.

---

# 20. Tests — frontend / transport

Existing files that must be updated as needed:

```text
tests/SteamInputAddonforClaw.Tests/ClawSensorProbe/ClawSensorProbeFrontendTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
```

Required coverage:

- mode is serialized through named pipe Start request;
- one session remains the only Runtime owner;
- repeated Open still does not create a competing session;
- Close still disposes active readers/callbacks;
- Start failure still finalizes a report;
- page/process shutdown behavior remains safe;
- snapshot maps backend/freshness/timing fields correctly;
- Axis mode still exposes current phase navigation;
- Live/Bias mode does not expose invalid Next/Previous operations;
- frontend does not receive raw sample history.

Keep the existing realistic lifecycle concurrency coverage around page close/runtime shutdown.

Do not add new epoch/barrier/state wrappers solely for theoretical RPC races if the current session gate already converges safely.

---

# 21. Expected files

Core diagnostic files:

```text
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeContracts.cs
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeCoordinator.cs
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeReaders.cs
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeSensorApi.cs
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeWorkflow.cs
```

A small new WinRT reader file is acceptable, e.g.:

```text
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeWinRtReaders.cs
```

Do not split every backend/interface into separate factories/managers unless actual code size demands it.

Frontend / transport:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml
src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml.cs
```

Tests:

```text
tests/SteamInputAddonforClaw.Tests/ClawSensorProbe/ClawSensorProbeTests.cs
tests/SteamInputAddonforClaw.Tests/ClawSensorProbe/ClawSensorProbeFrontendTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
```

---

# 22. Explicit non-goals

Do not implement in this work:

- production `MotionState`;
- Steam Deck `AccelX/Y/Z` or `Pitch/Yaw/Roll` publication;
- quaternion synthesis;
- synthetic gravity;
- production gyro bias subtraction;
- A2VM bias constants;
- A2VM axis transform constants;
- gyro-to-mouse / gyro-to-stick;
- sensitivity / deadzone / smoothing settings;
- per-game gyro profiles;
- gyro resampling into the 250 Hz Steam Deck publisher;
- sensor fusion framework;
- automatic suspend/resume sensor recovery;
- persistent calibration database;
- new VIIPER ABI;
- Full1902 authority changes.

Production SD6-B starts only after real CG3EM evidence has been collected with this diagnostic.

---

# 23. Hardware validation checklist after merge

Run on the repaired-driver CG3EM device.

Preserve output folders for each run.

### Run 1 — Live Sanity

Capture:

- all discovery evidence;
- selected gyro backend/identity;
- selected accel backend/identity;
- raw XYZ;
- fresh Hz;
- duplicate/no-data counts;
- worst read duration;
- worst sensor age;
- accel magnitude if g is proven.

### Run 2 — Axis Characterization

Complete all seven phases:

```text
REST
ROLL_LEFT
ROLL_RIGHT
PITCH_UP
PITCH_DOWN
YAW_LEFT
YAW_RIGHT
```

Verify per-phase summaries make the sign/axis relationship obvious.

Do not commit a production transform yet from one run.

### Run 3 — Stationary Bias, flat

Keep device untouched long enough to obtain stable statistics.

Preserve gyro mean/stddev/span and accel stability.

### Run 4 — Stationary Bias, different tilt

Repeat independently.

Compare whether gyro mean remains similar while gravity components change.

### Run 5 — Runtime restart

Close/restart Runtime and confirm a new session re-discovers the sources.

### Run 6 — Sleep / resume

Do not attempt to keep the same diagnostic session authoritative across suspend.

After resume start a new session and confirm sensor re-discovery / fresh capture.

---

# 24. Acceptance criteria

The work is complete when:

- Claw Sensor Probe remains developer-only and read-only.
- it is still gated by MSI Claw family, not production routing state.
- broad legacy discovery remains visible.
- direct type lookup is represented.
- WinRT Gyrometer is representable and usable as a gyro backend.
- WinRT Accelerometer is representable when available.
- gyro and accelerometer may use different backends.
- ambiguous candidates are reported rather than silently selected.
- raw values, fresh rate, age, and read duration are visible.
- duplicate/no-data/read-failure counts are preserved.
- quiet/duplicate reports do not automatically become the old generic five-second terminal timeout.
- actual backend failure still finalizes the session with an exact error.
- schema version 2 report identifies each selected backend/source.
- Axis mode preserves the existing seven-step manual workflow.
- per-phase mean/min/max/span are recorded.
- Stationary Bias mode records gyro mean/stddev/span and accelerometer stability.
- no bias is applied to samples.
- no A2VM constants become CG3EM production assumptions.
- page close/runtime shutdown still releases readers/callbacks and finalizes safely.
- no production controller/VIIPER code consumes probe output.
- full test suite and Release build pass.

After hardware validation, write a separate CG3EM source-contract document containing only **measured CG3EM facts**:

```text
gyro backend + identity + unit
accel backend + identity + unit
axis/sign transform
practical cadence
quiet/stall behavior
zero-rate offset evidence
required freshness semantics
```

Only then begin production SD6-B `MotionState` implementation.
