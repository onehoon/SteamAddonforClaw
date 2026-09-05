# Work Order — SD6A PR B: Claw Sensor Probe Capture Modes, Characterization Summaries, and Developer UI

## Status

**Next implementation work order after PR #495.**

This PR completes the developer-only **Claw Sensor Probe / Gyro Test** characterization workflow started by SD6A PR A.

PR A has already landed on `main` as:

```text
9ee8dd20a71e9434473afa842f8b3489c83adcac
SD6A PR A: Backend-aware Claw Sensor Probe discovery/readers/timing (schema v2) (#495)
```

This work order was prepared against that exact `main` baseline.

PR B adds only the remaining user-facing diagnostic workflow:

```text
Live Sanity
Axis Characterization
Stationary Bias
```

plus the per-phase / stationary-bias summaries needed to analyze real CG3EM data.

It does **not** implement production gyro/IMU output.

Hardware acceptance on a repaired-driver CG3EM is a **post-merge validation step** and is not a code-review / merge blocker for this PR. The implementation must be structurally correct and test-covered without requiring CI to have MSI Claw motion hardware.

---

# 1. Required reading / authority

Before implementation, read these documents and current code together.

Current authority order:

1. current `main` source code;
2. `docs/Full 1902 Implementation/README.md`;
3. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`;
4. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`;
5. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`;
6. `docs/gyro/GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md`;
7. `docs/gyro/SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md`;
8. this PR-B work order for the exact remaining implementation scope after PR A.

The broad characterization work order remains the design source, but statements describing the pre-PR-A code are historical now. This document narrows that plan to the actual code that exists after PR #495.

---

# 2. Product / diagnostic decision

Keep exactly one Runtime-owned Claw Sensor Probe session.

The user chooses the diagnostic purpose once at Start:

```text
Live Sanity
Axis Characterization
Stationary Bias
```

That choice is sent through the existing `StartClawSensorProbe` RPC and becomes Runtime session state.

Do **not** create:

- one coordinator per mode;
- one RPC per mode;
- a persisted gyro-test setting;
- a UI-side mode authority after Start;
- a generalized diagnostic state-machine framework.

The ownership remains:

```text
Developer UI
    ↓
existing frontend RPC
    ↓
one Runtime-owned ClawSensorProbeCoordinator
    ↓
existing backend-aware readers from PR A
    ↓
one session writer / CSV / JSON report
```

The mode changes only **how accepted diagnostic samples are grouped and summarized** and how the existing page presents/navigation-controls the session.

---

# 3. Full1902 isolation — hard boundary

This PR must not change or depend on production controller authority.

Do not modify:

- MSI Center M Enabled/Disabled authority;
- desired PID1901 / PID1902 state;
- DirectInput controller ownership;
- HidHide baseline or ownership;
- VIIPER ownership or teardown;
- X360 ↔ Steam Deck presentation switching;
- `CanonicalSteamDeckInputPublisher` cadence;
- Steam/BPM detection;
- rumble/haptics;
- `SteamDeckDeviceStateMapper` motion fields;
- any production gyro source or `MotionState`.

The Claw Sensor Probe remains a developer diagnostic gated on MSI Claw family identity. It must not become a second controller owner or motion authority.

---

# 4. Current-code baseline after PR A

## 4.1 Already implemented — do not redo

PR #495 already provides:

- backend-aware gyro / accelerometer selection;
- WinRT `Gyrometer` / `Accelerometer` support;
- legacy Sensor API broad + direct-type evidence;
- mixed-backend capture;
- finite-value validation;
- legacy XYZ/support-state validation;
- read-duration measurement;
- Fresh / Duplicate / NoData / Failure classification;
- duplicate/no-data non-terminal stale behavior;
- immutable timing snapshots at teardown;
- source minimum/requested/effective interval evidence;
- actual `sensor_age_ms` from the sensor timestamp;
- schema-v2 discovery / selected-source / timing output;
- named enum serialization;
- legacy custom data-key scoping;
- acceleration magnitude only when the source unit basis is proven as `G`;
- selected-source `SelectionReason`.

Do not refactor these areas merely because PR B touches adjacent files.

In particular, there is no need to redesign:

```text
ClawSensorProbeReaders
ClawSensorProbeSensorApi
ClawSensorProbeWinRtSources
```

unless a very small compile-time or snapshot-exposure change is required.

## 4.2 Existing naming distinction that must be preserved

Current code already has:

```csharp
internal enum ClawSensorCaptureMode
{
    Inactive,
    Transition,
    Recording
}
```

This describes the **sample recording state** written into CSV.

PR B needs a different concept: the **diagnostic session purpose**.

Do not reuse or repurpose `ClawSensorCaptureMode`.

Add a separate internal enum, e.g.:

```csharp
internal enum ClawSensorProbeMode
{
    LiveSanity,
    AxisCharacterization,
    StationaryBias
}
```

The frontend receives the corresponding public contract enum:

```csharp
public enum FrontendClawSensorProbeMode
{
    LiveSanity,
    AxisCharacterization,
    StationaryBias
}
```

---

# 5. Required mode semantics

## 5.1 Live Sanity

Purpose:

> Confirm that the selected physical gyro and accelerometer are producing usable current data and expose the timing/freshness evidence needed for inspection.

Flow:

```text
Open page
→ choose Live Sanity
→ Start
→ discover/select sources
→ open existing PR-A readers
→ immediately enter Recording
→ continue until Stop / page close / backend failure
→ finalize report
```

No 3-second countdown.

No Previous / Next.

No fake seven-phase traversal.

The CSV remains useful raw evidence, but JSON does not pretend these samples belong to REST or an axis phase.

Expected JSON:

```text
CaptureMode = LiveSanity
Phases = []
PerPhaseSummaries = []
StationaryBiasSummary = null
```

Global source/timing summaries remain available.

## 5.2 Axis Characterization

Preserve the current seven-phase workflow exactly:

```text
REST
ROLL_LEFT
ROLL_RIGHT
PITCH_UP
PITCH_DOWN
YAW_LEFT
YAW_RIGHT
```

Flow:

```text
Start
→ discovery/readers
→ existing 3-second countdown
→ record current phase
→ Previous / Next
→ next countdown
→ ...
→ Finish Test
→ finalize
```

Do not redesign the current phase/navigation behavior.

Back/revisit continues to create a new pass for that phase. The final summary must preserve pass identity rather than overwriting an earlier visit.

Expected JSON:

```text
CaptureMode = AxisCharacterization
Phases = existing visit log
PerPhaseSummaries = populated
StationaryBiasSummary = null
```

## 5.3 Stationary Bias

Purpose:

> Collect one explicit stationary run that can later be analyzed as a gyro zero-rate bias candidate and accelerometer stability check.

Flow:

```text
Open page
→ choose Stationary Bias
→ Start
→ discover/select sources
→ immediately enter Recording
→ user keeps handheld still
→ user presses Stop
→ finalize report
```

No 3-second countdown.

No Previous / Next.

No phase wizard.

Do not apply the measured bias anywhere.

Expected JSON:

```text
CaptureMode = StationaryBias
Phases = []
PerPhaseSummaries = []
StationaryBiasSummary = populated
```

---

# 6. Runtime mode ownership

The mode is an immutable property of one started session.

Recommended shape:

```csharp
coordinator.Start(ClawSensorProbeMode mode, root);
```

or equivalent.

Rules:

- `OpenClawSensorProbeAsync()` does not commit a mode.
- the page may locally show `Live Sanity` as the default selector while the session is only `Ready`;
- `StartClawSensorProbeAsync(mode)` sends the requested mode;
- after the Runtime accepts Start, the coordinator / writer is the authority for that session's mode;
- the mode selector becomes disabled while a session is running;
- changing the selector after completion applies only to the next fresh session;
- do not persist this selection in app settings.

A second Start on the same already-started coordinator should remain invalid under the existing one-session lifecycle rather than changing mode in-place.

---

# 7. Minimal workflow change

Current `ClawSensorProbeWorkflow.Start()` always creates phase 0 and enters `Countdown`.

Change only enough to express the three modes.

A suitable implementation is:

```text
Start(AxisCharacterization)
→ CurrentIndex = 0
→ Visit(REST)
→ Countdown

Start(LiveSanity / StationaryBias)
→ CurrentIndex = -1
→ no Visit()
→ Starting
```

Then:

```text
BeginRecording(Axis)
  requires Countdown
  → RecordingPhase

BeginRecording(Live/Bias)
  requires Starting
  → RecordingPhase
```

`Next()` / `Back()` remain Axis-only operations.

Do not make fake `REST` visits for Live/Bias merely to reuse existing code.

Do not introduce new mode-specific workflow classes.

The existing state enum can remain:

```text
Idle / Discovering / Ready / Starting / Countdown /
RecordingPhase / Stopping / Completed / Failed
```

No new hierarchical state machine is required.

---

# 8. Coordinator changes

Preserve the existing owner and lifecycle gates.

Current important invariants must remain:

- one `ClawSensorProbeCoordinator` per frontend session;
- one `_lifecycleGate`;
- one `_navigationGate`;
- terminal failure cleanup/finalization is non-cancellable;
- reader shutdown remains bounded;
- API release remains deferred if a reader is still blocked after bounded teardown;
- process shutdown / page close still dispose the Runtime-owned session.

Required mode-aware changes:

### Start

```text
Start(mode)
→ create writer with mode
→ start workflow with mode
```

### After source discovery

Axis:

```text
StartCaptureAsync
→ existing readers
→ CountdownAsync
→ BeginRecording
```

Live / Bias:

```text
StartCaptureAsync
→ existing readers
→ BeginRecording immediately
```

### BeginRecording

Axis:

- preserve `BeginRecordingPhase(...)`;
- set context to Recording with current phase/pass.

Live / Bias:

- enter Recording without `Workflow.Visits.Last()`;
- do not call `BeginRecordingPhase`;
- the reader context may keep an internal placeholder phase value for the existing sample type if convenient, but the writer must not serialize that placeholder as real phase evidence.

### Stop / Fail

Current code assumes a recording session always has `Workflow.Visits.Last()`.

Change this safely:

```text
if RecordingPhase && mode == AxisCharacterization
    → end current phase

if RecordingPhase && mode is LiveSanity/StationaryBias
    → set capture state inactive
    → no phase-end operation
```

Then preserve the existing cleanup/finalize path.

Do not duplicate Stop/Fail implementations by mode.

---

# 9. Writer aggregation — use the existing single writer

The current `ClawSensorProbeSessionWriter.WriteLoopAsync()` is the single consumer of accepted sample rows.

Use that fact to keep PR B simple.

Do **not** add another aggregation thread, lock hierarchy, analysis service, or post-processing manager.

Recommended small helper:

```csharp
internal sealed class ClawSensorVectorAccumulator
{
    long Count;
    double SumX, SumY, SumZ;
    double SumSqX, SumSqY, SumSqZ;
    double MinX, MinY, MinZ;
    double MaxX, MaxY, MaxZ;
    double FirstElapsedMs, LastElapsedMs;
    double IntervalTotalMs;
    long IntervalCount;

    // optional known-g magnitude fields
}
```

Because all accumulator mutation can happen inside `WriteLoopAsync()` and `FinalizeAsync()` awaits `_writerTask`, no new synchronization is required for these summary accumulators.

The accumulator should emit an immutable summary with the needed calculations.

Do not re-open or parse the CSV at finalization to calculate summaries.

---

# 10. Axis per-phase summaries

For Axis mode, aggregate accepted **Recording** samples by:

```text
(Phase, Pass, Sensor)
```

This is important because `Previous` can revisit a phase and create a second pass.

For each source within each visit, JSON must contain at least:

```text
Phase
Pass
Sensor                  // GYRO / ACCEL
Backend
SampleCount
MeanX / MeanY / MeanZ
MinX / MinY / MinZ
MaxX / MaxY / MaxZ
SpanX / SpanY / SpanZ
DurationMs
EffectiveHz
StartElapsedMs
EndElapsedMs
```

For accelerometer only when the selected source has:

```text
UnitBasis == G
```

also include a magnitude summary, preferably:

```text
MagnitudeGMean
MagnitudeGMin
MagnitudeGMax
MagnitudeGSpan
```

Do not infer `G` for a legacy source whose unit basis remains `Unknown`.

Do not derive or persist the final CG3EM axis transform in this PR.

The report should make manual comparison straightforward:

```text
REST vs ROLL_LEFT / ROLL_RIGHT
REST vs PITCH_UP / PITCH_DOWN
REST vs YAW_LEFT / YAW_RIGHT
```

but the transform decision remains a later evidence step.

---

# 11. Stationary Bias summary

Only `StationaryBias` Recording samples contribute to this summary.

## Gyroscope

Report:

```text
SampleCount
DurationMs
EffectiveHz
MeanX / MeanY / MeanZ
StandardDeviationX / Y / Z
MinX / Y / Z
MaxX / Y / Z
SpanX / Y / Z
```

This is a **bias candidate**, not calibration.

## Accelerometer

Report:

```text
SampleCount
DurationMs
EffectiveHz
MeanX / MeanY / MeanZ
MinX / Y / Z
MaxX / Y / Z
SpanX / Y / Z
```

If and only if `UnitBasis == G`, also report:

```text
MagnitudeGMean
MagnitudeGMin
MagnitudeGMax
MagnitudeGSpan
```

Do not:

- subtract these gyro means from live data;
- persist them to settings;
- create calibration profiles;
- compare them against A2VM's measured offset as an acceptance threshold;
- automatically decide production bias policy.

The later human analysis will compare at least two independent CG3EM captures.

---

# 12. Existing REST summary

PR A currently emits a `RestSummary` from samples whose phase is `REST`.

After PR B:

- Axis mode may keep `RestSummary` for convenience if it is derived from the Axis REST visit;
- Live Sanity must not populate `RestSummary` from its placeholder/internal phase value;
- Stationary Bias must use `StationaryBiasSummary`, not masquerade as REST.

If the new per-phase accumulator makes `_restGyro` / `_restAccel` lists redundant, remove those lists and derive `RestSummary` from the first/current REST pass summary rather than retaining duplicate accumulation paths.

Prefer one summary authority inside the writer.

---

# 13. Report schema

Keep:

```text
SchemaVersion = 2
```

PR A established schema v2 for this same SD6A feature; PR B completes the remaining v2 fields. Do not bump to v3 solely because the split PR now fills the originally planned mode/summaries.

Add top-level:

```text
CaptureMode
PerPhaseSummaries
StationaryBiasSummary
```

Preserve all PR-A fields and semantics:

```text
Discovery
SensorDiscovery
SelectedGyroscope
SelectedAccelerometer
SourceConfiguration
LegacyCustomDataKeys
GyroscopeSummary
AccelerometerSummary
TimingSummary
DroppedSampleCount...
ShutdownTimedOut
Errors
Warnings
```

Recommended mode-dependent shape:

```text
LiveSanity:
  CaptureMode = LiveSanity
  Phases = []
  PerPhaseSummaries = []
  StationaryBiasSummary = null

AxisCharacterization:
  CaptureMode = AxisCharacterization
  Phases = populated
  PerPhaseSummaries = populated
  StationaryBiasSummary = null

StationaryBias:
  CaptureMode = StationaryBias
  Phases = []
  PerPhaseSummaries = []
  StationaryBiasSummary = populated
```

Enums remain serialized as names through the PR-A `JsonStringEnumConverter`.

---

# 14. CSV contract

Current CSV `capture_mode` means:

```text
Inactive / Transition / Recording
```

Do not change that meaning.

Add a separate stable column for the session diagnostic mode, e.g.:

```text
probe_mode
```

Recommended header shape:

```text
sequence,utc_timestamp,elapsed_ms,probe_mode,capture_mode,phase,phase_pass,
sensor,x,y,z,sample_interval_ms,sensor_timestamp,backend,read_duration_ms,sensor_age_ms
```

For Axis mode:

- phase + pass are populated normally.

For Live/Bias:

- `phase` and `phase_pass` should be empty in emitted CSV rows if practical;
- do not label all samples as REST merely because the current internal sample record requires a phase placeholder.

If changing the sample contract to nullable phase creates disproportionate churn, keep the internal placeholder but make the writer's CSV/report projection mode-aware. The external artifact is what must not claim false phase evidence.

---

# 15. Frontend Start contract

Current contract:

```csharp
Task<FrontendClawSensorProbeSnapshot> StartClawSensorProbeAsync(
    CancellationToken cancellationToken = default);
```

Change to:

```csharp
Task<FrontendClawSensorProbeSnapshot> StartClawSensorProbeAsync(
    FrontendClawSensorProbeMode mode,
    CancellationToken cancellationToken = default);
```

The existing RPC method remains:

```text
StartClawSensorProbe
```

Add one request DTO:

```csharp
internal sealed record StartClawSensorProbeRequest(
    FrontendClawSensorProbeMode Mode);
```

Do not add:

```text
StartClawSensorProbeLive
StartClawSensorProbeAxis
StartClawSensorProbeBias
```

---

# 16. Frontend transport protocol

Current protocol is:

```text
FrontendTransportProtocol.CurrentVersion = 25
```

PR B changes a required frontend contract and changes `StartClawSensorProbe` from no payload to a required mode payload.

Bump to:

```text
Version 26
```

Add a protocol history comment explaining:

```text
Version 26:
Claw Sensor Probe adds FrontendClawSensorProbeMode,
StartClawSensorProbe now requires StartClawSensorProbeRequest,
and the probe snapshot/candidate contracts expose mode/backend/freshness/timing evidence.
Pre-release: no compatibility shim.
```

Server changes:

- remove `StartClawSensorProbe` from the group that rejects any non-null payload;
- `StartClawSensorProbe` must require/decode `StartClawSensorProbeRequest`;
- pass the decoded mode to `_inner.StartClawSensorProbeAsync(mode, token)`;
- missing/malformed mode payload fails as `InvalidMessage` through the existing protocol path.

Client changes:

```text
StartClawSensorProbeAsync(mode)
→ send StartClawSensorProbeRequest(mode)
```

Do not loosen generic payload validation for unrelated RPCs.

---

# 17. Frontend snapshot — compact evidence only

The current snapshot is intentionally compact. Keep it that way.

## Candidate projection

Extend `FrontendClawSensorProbeCandidate` with the PR-A facts the UI now needs:

```text
Backend
State
DevicePath
UnitBasis
SelectionReason
```

It is acceptable to project Backend / UnitBasis as public typed enums or stable strings. Prefer typed enums if that remains simple and keeps transport self-describing.

Do not send every legacy query object/candidate property to the UI merely because the JSON report contains it.

## Axis/live source snapshot

Extend the current latest-value snapshot narrowly with:

```text
FreshAgeMs
LastReadDurationMs
IsFresh
MagnitudeG?       // accelerometer only when UnitBasis == G
```

`IsFresh` is a diagnostic presentation fact, not production freshness authority.

Use the existing PR-A stale-warning threshold and evidence. A sensible condition is:

```text
FreshCount > 0
AND FreshAgeMs < StaleWarningThreshold
```

Do not create another persisted freshness threshold.

## Timing summary

Expose a compact per-source timing DTO containing the existing PR-A evidence needed by the UI:

```text
FreshCount
DuplicateCount
NoDataCount
ReadFailureCount
EffectiveFreshHz
LastReadDurationMs
MaxReadDurationMs
FreshAgeMs
MaxFreshAgeMs
LongReadCount
```

Do not send raw attempt history.

## Session fields

Add at least:

```text
Mode?        // null while Ready before Start is acceptable
ElapsedMs
```

For Stationary Bias completion, add one compact frontend summary sufficient to display:

- gyro mean / standard deviation / span;
- accel span and known-g magnitude stability;
- count / effective rate.

Detailed per-phase summaries do **not** need to cross the named pipe. They belong in the JSON report.

---

# 18. Exposing current/final timing from the coordinator

Do not move timing ownership out of `ClawSensorProbeReaders`.

While readers are active, compact UI projection may call:

```text
GyroscopeTiming.Snapshot()
AccelerometerTiming.Snapshot()
```

through narrow coordinator accessors.

After Stop/Fail, the writer already owns the frozen teardown-boundary snapshots from PR A. If the completion UI needs final timing values, expose those immutable writer snapshots through a read-only property rather than keeping the disposed reader object alive.

Likewise, do not retain WinRT/COM sensor handles solely so the UI can display completed-session metadata.

---

# 19. UI changes

Files:

```text
src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml
src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml.cs
```

Keep the page utilitarian. This is a developer diagnostic, not a polished telemetry dashboard.

## Mode selector

Add one selector above Start:

```text
Live Sanity
Axis Characterization
Stationary Bias
```

Default selection:

```text
Live Sanity
```

The selector is enabled only while the Runtime session is `Ready`.

Start sends the selected mode through the Start RPC.

Once started, render the mode returned by the Runtime snapshot as authoritative.

## Live Sanity UI

Show compactly:

```text
Gyro:
backend | identity | XYZ | effective fresh Hz | fresh age | last read duration

Accel:
backend | identity | XYZ | effective fresh Hz | fresh age | last read duration
|g| only when UnitBasis == G
```

Hide or disable Previous / Next.

Stop finalizes.

## Axis Characterization UI

Preserve the existing UI flow:

- 3-second countdown;
- current phase instruction;
- Previous;
- Next / Finish Test.

Do not redesign the existing phase instruction text unless needed for mode layout.

## Stationary Bias UI

Instruction:

```text
Place the device still on a stable surface.
Leave it untouched during the capture, then press Stop.
```

Show:

- elapsed capture time;
- raw XYZ;
- effective fresh rate;
- freshness age;
- read duration;
- known-g magnitude when valid.

Hide or disable Previous / Next.

## Completion summary

Display compact evidence:

- mode;
- selected gyro backend / identity;
- selected accel backend / identity;
- effective fresh rates;
- duplicate / no-data / read-failure counts;
- maximum read duration;
- maximum fresh age;
- dropped rows;
- output directory;
- for Bias: gyro mean/stddev/span and accel stability/magnitude summary.

Do not render all per-phase summary arrays in the page. The JSON report is the authoritative detailed artifact.

---

# 20. Preserve current frontend lifecycle protections

The existing page/frontend behavior contains important realistic lifecycle fixes from prior reviews. Do not regress them.

Preserve:

```text
Activate
→ Open Runtime session
→ start ~200ms page-local polling
```

Polling remains:

- ~200 ms;
- single-flight;
- compact snapshot only.

Do not poll at gyro cadence.

Page exit remains:

```text
stop poll timer
→ cancel in-flight Start/Next/Previous/Open
→ CloseClawSensorProbeAsync
→ Runtime stops/finalizes/disposes session
```

The cancellation-before-Close ordering must remain because the named-pipe server serializes operations through one operation gate.

Transport loss must still:

- stop the poll timer;
- disable mutation controls;
- show the Runtime-disconnected error;
- not continue retrying every 200 ms with stale telemetry.

Frontend disconnect cleanup in `NamedPipeAddonFrontendServer` must still retire an open Runtime-owned probe session.

---

# 21. Real lifecycle behavior

## Backend / device failure

PR A already makes an actual backend read failure terminal.

Preserve:

```text
backend read/handle failure
→ reader error
→ frontend polling reconciles via FailOnReaderFaultAsync
→ non-cancellable terminal cleanup
→ report finalized
```

Do not add an automatic retry loop.

## Runtime restart

A fresh session must run discovery again.

Do not persist:

- COM sensor ID authority;
- WinRT sensor object identity;
- selected backend authority;
- mode as settings.

## Sleep / hibernate / resume

This is a real product lifecycle, but PR B does not need a new diagnostic power manager.

Required product-level outcome:

- do not knowingly present pre-suspend stale motion as fresh;
- if the backend/device invalidates and reports an actual failure, finalize the active session through the existing failure path;
- after resume, a new user-started session re-discovers sensors.

Do **not** add:

- `ClawSensorProbePowerManager`;
- automatic resume retry/rebind state machine;
- new epochs/barriers merely to keep a developer diagnostic alive across suspend.

Hardware sleep/resume acceptance is post-merge validation.

---

# 22. Tests — core / writer

Primary file:

```text
tests/SteamInputAddonforClaw.Tests/ClawSensorProbe/ClawSensorProbeTests.cs
```

Keep all PR-A tests passing.

Add focused tests for:

### Mode/workflow

1. Axis Start still enters first-phase Countdown and visits REST.
2. Live Start does not create a phase visit or countdown.
3. Bias Start does not create a phase visit or countdown.
4. Live/Bias begin recording directly after source startup.
5. Next/Previous cannot mutate a Live/Bias session.
6. Axis Previous still creates a new pass rather than replacing the earlier pass.

### Writer / report

7. Schema remains version 2.
8. `CaptureMode` serializes as the named mode.
9. Live report emits empty `Phases` / `PerPhaseSummaries` and null Bias summary.
10. Axis report emits phase/pass/source summaries.
11. Revisited phase passes remain distinct in `PerPhaseSummaries`.
12. Per-phase mean/min/max/span calculations are correct.
13. Per-phase duration/effective-rate calculations are correct.
14. Bias gyro mean/stddev/min/max/span calculations are correct.
15. Bias accel min/max/span calculations are correct.
16. Bias accel magnitude mean/min/max/span exists only for UnitBasis `G`.
17. Unknown-unit accelerometer never emits a `MagnitudeG*` field.
18. Live/Bias samples do not create fake REST phase summaries.
19. CSV has a separate `probe_mode` column while retaining the existing `capture_mode` meaning.
20. Live/Bias CSV does not claim a real phase/pass when no axis phase exists.
21. Stop/Fail still finalizes exactly one report.
22. Existing PR-A TimingSummary / Discovery / SourceConfiguration fields remain intact.

Prefer pure deterministic accumulator tests over test-only threads or timing machinery.

Do not add instruction-level race tests unrelated to a real supported lifecycle path.

---

# 23. Tests — frontend / transport

Existing files:

```text
tests/SteamInputAddonforClaw.Tests/ClawSensorProbe/ClawSensorProbeFrontendTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
```

## Transport

Required:

1. protocol version is 26.
2. Start sends `FrontendClawSensorProbeMode` through the existing Start RPC payload.
3. Start with missing payload is rejected.
4. malformed / unknown mode is rejected by the existing strict enum JSON policy.
5. unrelated no-payload RPC validation remains unchanged.
6. no raw sample-history collection exists in the frontend transport contract.

## Frontend

Preserve all existing lifecycle coverage, including:

- MSI Claw-family gate independent of production compatibility status;
- repeated Open does not create a competing session;
- Close disposes session;
- Start failure in hardware-less tests still finalizes a report and returns Failed;
- Capture after Failed remains stable;
- device/hardware metadata reaches the report;
- process shutdown during Start does not deadlock or leak unexpected exceptions;
- Open after Completed/Failed creates a fresh session;
- shutdown session-commit barrier remains intact;
- Capture/process-shutdown cleanup remains safe;
- Runtime diagnostic backend remains WinUI-free.

Add mode-specific checks:

7. requested mode is stored/projected by the Runtime session.
8. Axis mode exposes phase navigation.
9. Live/Bias mode does not expose valid phase navigation.
10. mode selector/start contract does not create a second Runtime owner.
11. compact snapshot maps candidate Backend/State/DevicePath/UnitBasis/SelectionReason.
12. compact snapshot maps live timing/freshness fields.
13. completed Bias snapshot exposes the compact Bias summary required by the UI.

Do not add new synchronization wrappers only to manufacture theoretical RPC races. The existing session gate / operation gate / shutdown barrier should remain the authority unless a reproducible supported-lifecycle defect is demonstrated.

---

# 24. Expected implementation files

Based on the actual post-PR-A code, the likely touched files are:

## Core diagnostic

```text
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeContracts.cs
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeCoordinator.cs
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeWorkflow.cs
```

`ClawSensorProbeReaders.cs` may need only narrow read-only timing exposure, if any.

Do not modify `ClawSensorProbeSensorApi.cs` or `ClawSensorProbeWinRtSources.cs` unless a real compile/integration need appears; PR A already owns those concerns.

## Frontend contracts / transport

```text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
```

## UI

```text
src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml
src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml.cs
```

## Tests

```text
tests/SteamInputAddonforClaw.Tests/ClawSensorProbe/ClawSensorProbeTests.cs
tests/SteamInputAddonforClaw.Tests/ClawSensorProbe/ClawSensorProbeFrontendTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
```

Do not split the work into extra service/factory/manager files merely to reduce LOC in individual files.

A practical target remains roughly <=500 net new/changed LOC where reasonable, but correctness of this one coherent diagnostic workflow is more important than artificially splitting it into new abstractions.

---

# 25. Explicit non-goals

Do not implement:

- production `MotionState`;
- Steam Deck `AccelX/Y/Z` publication;
- Steam Deck `Pitch/Yaw/Roll` publication;
- quaternion synthesis;
- synthetic gravity;
- gyro bias subtraction;
- persisted gyro calibration;
- A2VM bias constants;
- A2VM axis-transform constants;
- auto-derived CG3EM transform;
- gyro-to-mouse;
- gyro-to-stick;
- sensitivity/deadzone/smoothing user settings;
- per-game gyro profiles;
- 250 Hz production motion resampling;
- sensor fusion;
- diagnostic suspend/resume recovery manager;
- automatic sensor retry/reconnect loop;
- new VIIPER ABI;
- any Full1902 authority or routing change.

---

# 26. Hardware validation after merge — non-blocking for PR B

The implementation PR may merge with normal build/test success even though CI cannot validate real CG3EM sensors.

After merge, run the following manually on the repaired-driver CG3EM and preserve each output directory.

## Run 1 — Live Sanity

Capture:

- selected backend/identity for both sources;
- raw XYZ;
- effective fresh Hz;
- fresh age;
- duplicate/no-data counts;
- worst read duration;
- sensor age;
- known-g acceleration magnitude if applicable.

## Run 2 — Axis Characterization

Complete all seven phases.

Confirm the per-phase summary makes sign/axis differences clear.

Do not commit the production transform from one run alone.

## Run 3 — Stationary Bias, flat

Keep device untouched and preserve:

- gyro mean/stddev/span;
- accel stability;
- timing/stall evidence.

## Run 4 — Stationary Bias, different tilt

Repeat independently and compare gyro mean stability while gravity components change.

## Run 5 — Runtime restart

Start a fresh session and verify discovery occurs again.

## Run 6 — Sleep / resume

Do not require the same diagnostic session to survive suspend.

After resume, start a fresh session and verify sensors are rediscovered and current data is fresh.

Any hardware-specific identity, unit, axis, bias, cadence, or lifecycle finding from these runs belongs in a later measured-CG3EM source-contract document before production SD6-B.

---

# 27. Acceptance criteria

PR B is complete when all of the following are true:

- baseline is current `main` after PR #495;
- Claw Sensor Probe remains developer-only and read-only;
- there is one Runtime-owned session/coordinator;
- the user can choose Live Sanity / Axis Characterization / Stationary Bias;
- mode is passed through one existing Start RPC, not separate mode RPCs;
- frontend protocol is bumped to v26;
- mode is not persisted as app authority/settings;
- Live/Bias start recording without fake phase countdown/navigation;
- Axis preserves the existing seven-phase countdown/navigation behavior;
- mode-aware Stop/Fail does not assume a phase visit exists;
- JSON remains `SchemaVersion = 2`;
- JSON adds named `CaptureMode`;
- Axis produces pass-aware per-phase source summaries;
- Bias produces gyro mean/stddev/min/max/span and accel stability summaries;
- accel magnitude exists only with proven `UnitBasis == G`;
- Live/Bias do not generate fake REST evidence;
- CSV distinguishes `probe_mode` from existing recording-state `capture_mode`;
- frontend snapshot remains compact and includes the required backend/freshness/timing evidence;
- the UI keeps ~200 ms single-flight polling;
- Previous/Next are available only in Axis mode;
- page close, frontend disconnect, process shutdown, reader failure and bounded reader teardown retain their current safe behavior;
- all existing PR-A tests continue to pass;
- new deterministic mode/summary/transport tests pass;
- no production controller, HidHide, VIIPER, Steam/BPM, rumble, or Steam Deck IMU code changes are introduced;
- CG3EM hardware acceptance is documented as post-merge validation, not a PR merge blocker.

---

# 28. Completion boundary

After PR B, SD6-A diagnostic tooling is complete enough to collect the evidence needed for the production motion design.

The next step is **not** to immediately wire raw motion into the Steam Deck publisher.

The next step is:

```text
run the completed diagnostic on repaired-driver CG3EM
→ preserve Live / Axis / Bias / restart / resume evidence
→ write one measured CG3EM source contract
   (backend, identity, fields, units, axes, cadence/freshness, bias policy)
→ only then prepare production SD6-B MotionState / Steam Deck IMU work
```

Do not skip that evidence boundary.
