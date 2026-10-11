# Work Order — Debug-Only Full1902 Gyro/Accelerometer Cadence, Jitter, and Native IMU Diagnostics

**Date:** 2026-10-11  
**Target:** one narrow local-Codex implementation PR, current main  
**Applies to:** MSI Claw 7 AI+ / 8 AI+ A2VM (msi.claw.a2vm.7 and msi.claw.a2vm.8); MSI Claw 8 EX AI+ / CG3EM (msi.claw.cg3em)  
**Purpose:** diagnose gyro-to-mouse choppiness / gyro jitter without changing input behavior  
**Status:** work order only; local Codex implements the code PR; user conducts post-merge hardware checks

## 1. Why this is needed (observed vs unknown)

The user received physical A2VM gyro feedback: gyro-to-right-joystick felt acceptable in Apex Legends, while gyro-to-mouse was choppy; an AntiMicroX observation suggested jitter. The exact observation stage in AntiMicroX is not established; do not treat it as a physical raw-sensor capture.

The user-owned Google Drive Addon/Log/AV2M recordings from October 9–11 confirm the following on A2VM 8:
- ControllerMotion MotionSourceStartAccepted and MotionSourcesSelected, selecting WinRtGyrometer and legacy Physical Accelerometer;
- canonical Steam Deck SetState publisher mostly at approximately 245–250 Hz, zero reported SetState failures in reviewed runs;
- **no logged production gyro/accel fresh-sample Hz, polling gaps, duplicate readings, XYZ variability, sensor timestamp age, or native-IMU freshness/reuse**.

Do not infer the physical gyro operates at 250 Hz from the Steam Deck publisher heartbeat. Prior EX/CG3EM probe captures observed ~98 Hz legacy gyro, ~273–276 Hz accel, occasional ~120–141 ms gyro GetData blocking reads, and a nonzero stationary raw gyro center. Those measurements **do not establish A2VM WinRT gyro characteristics**.

The current production ReadRoleAsync calls handle.Read(), filters no-data/duplicate reports, publishes a sample, and then WaitForNextRead(candidate MinimumReportInterval, default 10 ms), also waiting after no-data/duplicate. The net effect of this additional wait has not been measured on A2VM or EX production. This work order instruments the existing policy **without changing it**, so a later measured fix can be narrow.

## 2. Authority, architecture, and references

Read before coding:
1. docs/Full 1902 Implementation/README.md and its current Full1902 authority documents;
2. docs/gyro/FULL1902_GYRO_PR1_PR2_IMPLEMENTATION_PLAN_2026-10-09.md;
3. docs/work-order/GYRO_PR1_FULL1902_COMMON_MOTION_CORE_WORK_ORDER_2026-10-09.md;
4. docs/work-order/GYRO_PR2_FULL1902_STEAMDECK_NATIVE_IMU_OUTPUT_WORK_ORDER_2026-10-09.md;
5. docs/gyro/CG3EM_LIVE_SANITY_HARDWARE_RESULTS_2026-09-05.md;
6. current main source/tests listed below.

This is a **standalone Full1902 app**. CTW is not a dependency or integration point. No change to physical ownership, PID1901/PID1902 restoration, HidHide, VIIPER ownership, Steam/Xbox360 switching, suspend/resume, recovery, rumble, controller mapping, or native ABI.

## 3. Reviewed code anchors

- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawMotionSource.cs
  - ReadRoleAsync, around lines 330–382: shared A2VM/EX production gyro and accel sensor threads; Read(), timestamp deduplication, PublishSample(), WaitForNextRead().
  - PublishSample, around lines 386–435: normalized immutable snapshot and monotonic receive timestamp.
  - PollDelayMilliseconds/WaitForNextRead, around lines 540–547: existing polling rule to measure, **not modify**.
  - RunGenerationAsync source selection and existing ControllerMotion lifecycle/error messages must be preserved.
- src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeWinRtSources.cs: WinRT Gyrometer.GetCurrentReading() timestamp and configuration; legacy COM GetData via ClawSensorProbeLegacySourceHandle.
- src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeContracts.cs: ClawSensorReportReadResult and ClawSensorProbeSourceConfiguration with Minimum/Requested/EffectiveReportIntervalMs. Preserve distinction between WinRT interval configuration and legacy metadata.
- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawMotionState.cs: LatestState freshness (gyro 250 ms, accel 500 ms); timestamp/flags neutralization.
- src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs: one motion snapshot and SteamDeckDeviceStateMapper.Map per 4 ms publisher operation; existing ~1 s Info-level Steam Deck output heartbeat; debug-only counters can be added here if strictly cheap.
- src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs: existing native signed fields; **no mapping changes**.
- src/SteamInputAddonforClaw/Diagnostics/AppLog.cs: AppLog.IsEnabled(AppLogLevel.Debug) and AppLog.Debug("ControllerMotion", ...). Existing bounded background logging queue: avoid per-sample/per-publish entries.
- tests/SteamInputAddonforClaw.Tests/MsiClawMotionSourceTests.cs and CanonicalSteamDeckInputPublisherTests.cs: existing fake readers, fake time and publisher tick seam.

## 4. Scope and strict logging policy

Implement one small diagnostic-only feature in the **existing production sensor reader**, with a small optional output-side summary in the **existing Steam Deck publisher**. Both use **Debug severity only**, not Info/Warn (existing production warnings remain unchanged).

- When Debug is disabled, no aggregation work beyond a cheap enable check; no per-sample allocations, timers, logging, or new polling.
- When Debug is enabled, collect cheap role-local counters/numeric aggregates; **never log one entry per sample, every 4ms publisher tick, or every time a timestamp repeats**.
- Emit **at most one summary per sensor role per 10 seconds** and **one publisher motion summary per 10 seconds** while running. At most three new Debug records per 10-second steady-state window. An optional final partial summary on an ordinary stop is allowed; never spin/wait solely for a log.
- Use existing AppLog and ControllerMotion/SteamOutput categories. No CSV/log exporter, new worker/timer/service, settings/UI toggle, sampling mode, persistent data, device inspector, extra lock/epoch, or configurable thresholds.
- Emit summary from each sensor's existing owning reader thread after its Read/Wait progression, and from the existing publisher thread for output statistics. Do not run sensor operations/log formatting inside the snapshot lock or from SetState.
- Do not let a diagnostic exception disable an input reader or fail a VIIPER publisher. Avoid a broad catch around normal reader logic; only make optional telemetry fail-passive where needed.
- Debug toggle during a session should start a fresh window when enabled and clear/discard aggregation when disabled; no stale summary on re-enable.

## 5. Required sensor-reader instrumentation — both gyro and accel, A2VM and EX

Add a tiny per-reader (thread-confined) accumulation helper or private struct/class close to MsiClawMotionSource; avoid introducing an independent diagnostics framework. Use existing injected monotonic _getTimestamp / _timestampFrequency, or Stopwatch where already used, consistently. Do not change PublishSample signature or ownership unless essential.

### 5.1 Source configuration record (once on actual opened handle, Debug only)

Event=MotionReaderDiagnosticsStarted; fields:
- Model, Role=Gyroscope/Accelerometer, Backend, SensorId;
- CandidateMinimumReportIntervalMs (raw candidate metadata; if unavailable say unavailable);
- WinRtMinimumReportIntervalMs, WinRtRequestedReportIntervalMs, WinRtEffectiveReportIntervalMs from handle.Configuration when applicable; leave them null/unavailable on LegacySensorApi;
- PollDelayMs from existing PollDelayMilliseconds; report this as **extra application wait after each Read**, never as proven physical sample interval;
- Gyro/Accel freshness thresholds if helpful as labels (250ms/500ms), not as a dynamic sensor property.

This establishes real backend configuration rather than incorrectly equating WinRT minimum with measured Hz.

### 5.2 Ten-second sensor role summary

Event=MotionReaderDiagnosticsSummary; Model, Role, Backend, WindowMs, ReadAttempts, FreshAcceptedCount, DuplicateTimestampCount, NoDataCount, MissingSensorTimestampCount, ActualFreshHz (FreshAcceptedCount / elapsed wall-clock monotonic seconds), configured PollDelayMs, and the following:

- ReadDurationMeanMs, ReadDurationMaxMs: time immediately before/after handle.Read(), including no-data and duplicate reads. Track elapsed for each successful Read attempt; existing MotionReaderFailed warning owns failed-read reporting.
- FreshReceiveIntervalMeanMs, FreshReceiveIntervalMaxMs from **consecutive accepted** samples' monotonic receive time. Include GapOver20MsCount, GapOver50MsCount, GapOver100MsCount to reveal delayed updates; use these as diagnostic bins, not freshness thresholds. Do not count an interval spanning a restart or the very first fresh point of a reader generation.
- SensorTimestampIntervalMeanMs/MaxMs between consecutive accepted timestamps when both are present and advance; count SensorTimestampNonMonotonicCount if timestamp goes backwards. Do not claim legacy millisecond timestamp granularity is equivalent to monotonic receive resolution.
- SensorAgeMeanMs/MaxMs from DateTimeOffset.UtcNow - sample.SensorTimestamp **when a timestamp exists**, computed after returning from Read. Count negative/skewed ages separately; do not silently convert clock-skew artifacts into positive latency or invalidate good data. Label age as diagnostic estimated wall-clock age, not authoritative validity. Avoid attaching UTC string per sample.
- Motion axis value **mean and standard deviation X/Y/Z** over *fresh accepted normalized* samples. For gyro, report in deg/s; for accel, in g. Label as window raw-input variation **after current axis normalization, before any bias/filtering**. These values are not automatically stationary noise: motion during a window increases deviation. If sample count < 2, stddev is unavailable/0 with count explicit.
- Optionally Min/Max per axis when cheap and useful; keep output compact. Do **not** dump continuous XYZ rows.

For one window, no data is informative: FreshAcceptedCount=0, NoDataCount and DuplicateTimestampCount may be nonzero. The summary cadence runs on the reader loop; a genuinely indefinitely blocked Read cannot emit until control returns. No watchdog just for telemetry.

Use mathematically stable, incremental mean/variance (e.g. Welford), or an equally simple bounded accumulator. Avoid allocations per sample. Only count FreshAcceptedCount if PublishSample succeeded; invalid/nonfinite remains governed by the existing warning path. Do not synthesize readings, timestamps, or samples.

### 5.3 Sample classification and existing behavior

- Keep separate successful ReadAttempt / accepted / duplicate / no-data counts. A duplicate timestamp from WinRT GetCurrentReading is expected when the poll is faster than sensor production; it is **not** itself a reader failure.
- Keep reader wait placement and duration unchanged: telemetry must show whether A2VM is actually under-sampled before anyone removes or reduces extra wait.
- Avoid blocking a legacy CG3EM COM GetData read, changing WinRT ReportInterval, altering source selection, or interfering with sensor handle release on the owning thread.
- Preserve existing immediate invalidation on suspend, physical loss, and controller teardown. Reset all counters on new reader generation; no aggregates shared across restart/re-enumeration.

## 6. Required output-side correlation (existing Steam Deck publisher)

Add one **Debug-only**, **10-second** Event=MotionImuPublishDiagnosticsSummary to the existing CanonicalSteamDeckInputPublisher using its current manual tick/worker behavior and current snapshot; piggyback existing publisher operations/heartbeat (no new timer). At minimal incremental per-4ms cost under Debug, count:
- PublishTickCount, ImuUsableTickCount, ImuUnavailableTickCount (using already captured post-freshness motion flags, not all-native-fields-zero: a stationary valid gyro can be zero);
- GyroUnavailableTickCount, AccelUnavailableTickCount independently;
- GyroUniqueReceiveTickCount, GyroRepeatedReceiveTickCount (compare positive nonzero GyroReceiveTicks to previous usable/available gyro receive timestamp; count repeats at ~250Hz as normal, not errors);
- optionally GyroAgeMaxMs/AccelAgeMaxMs from current monotonic _timestampProvider vs existing receive ticks, only if no extra clock/source reads or heavy arithmetic are required.

Expose enough labels to correlate actual fresh physical gyro Hz with already-present Info-level EffectiveSetStateHz. Do not add separate logic to re-read the physical sensor, map IMU twice, log native gyro values every tick, call GetCurrentReading, or change the 250Hz timer/publisher fault policy. Leave existing Info heartbeat exactly as-is.

If the published snapshot is unavailable or the provider throws (existing fail-passive handling), simply count unavailable; do not turn a diagnostic failure into a publisher failure. Reset counters per publisher start and when Debug is toggled.

## 7. Suggested log shape (field names are a contract, example values are NOT measured)

~~~text
[DEBUG] [ControllerMotion] MotionReaderDiagnosticsStarted Event=MotionReaderDiagnosticsStarted Model=msi.claw.a2vm.8 Role=Gyroscope Backend=WinRtGyrometer CandidateMinimumReportIntervalMs=... WinRtRequestedReportIntervalMs=... WinRtEffectiveReportIntervalMs=... PollDelayMs=...
[DEBUG] [ControllerMotion] Motion reader diagnostic summary Event=MotionReaderDiagnosticsSummary Model=msi.claw.a2vm.8 Role=Gyroscope Backend=WinRtGyrometer WindowMs=10000 ReadAttempts=... FreshAcceptedCount=... DuplicateTimestampCount=... NoDataCount=... ActualFreshHz=... ReadDurationMeanMs=... ReadDurationMaxMs=... FreshReceiveIntervalMeanMs=... FreshReceiveIntervalMaxMs=... GapOver20MsCount=... GapOver50MsCount=... GapOver100MsCount=... SensorAgeMaxMs=... GyroMeanX=... GyroStdDevX=...
[DEBUG] [SteamOutput] Motion IMU publish diagnostic summary Event=MotionImuPublishDiagnosticsSummary PublishTickCount=... ImuUsableTickCount=... GyroUniqueReceiveTickCount=... GyroRepeatedReceiveTickCount=... GyroUnavailableTickCount=... AccelUnavailableTickCount=...
~~~

Use real numeric/nullable formatting consistent with AppLog; ensure non-applicable fields are explicitly absent or null, not misleading fake zero timings. The test implementation may choose reasonable concise names, but document the final fields in the PR so log consumers can recognize them.

## 8. Required deterministic unit tests — no hardware requirement

Extend existing tests rather than constructing an integration infrastructure. Factor only the smallest pure accumulator seam needed for deterministic fake-clock tests.

1. Per-model reader selection: A2VM 7/8 WinRT gyro + legacy accel and CG3EM legacy gyro + legacy accel are both instrumented through common reader path; source backend and role labels correct.
2. Monotonic fake-clock 10-second summary: correct FreshAcceptedCount, ActualFreshHz, mean/max receive gap and >20/>50/>100ms counts; first sample does not produce an artificial gap.
3. NoData, repeated identical sensor timestamps, missing timestamp, and a backwards timestamp: correct classifications, no false fresh increment, and no crash.
4. Artificially blocked Read can create a long ReadDuration and following receive gap, but never blocks LatestState (reuse existing blocking-reader seam); logging adds no extra worker.
5. Mean/stddev with constant stationary gyro vector produces nonzero mean / zero standard deviation; alternating fake values create expected nonzero variation; accel uses g and gyro uses deg/s.
6. Missing/negative/skewed sensor age is labeled and does not impact normalization, validity, or freshness.
7. Debug disabled means no new log entries and no diagnostic accumulation; Info/Warn settings preserve existing output; toggling Debug resets diagnostics and avoids old partial-window carryover.
8. Manual-tick publisher with repeated positive GyroReceiveTicks, newer timestamp, valid-zero gyro, gyro-stale and accel-stale snapshots produces the correct unique/repeat/available counts and no difference in the native mapped reports. No non-debug per-tick extra work.
9. Stop/restart, one reader failure, sensor reader blocked during cancel, suspend/physical loss and subsequent reacquisition preserve existing fail-passive behavior, with no cross-generation aggregation or additional wait.

Use existing MsiClawMotionSourceTests and CanonicalSteamDeckInputPublisherTests, plus one tiny accumulator test if needed. Do not make tests dependent on DateTime.Now wall clock, sleeps, hardware, Steam installation, or a real VIIPER device. Do not require an exact number of debug records at instruction-level racing boundaries.

## 9. Explicit non-goals

Do **not** in this PR:
- change PollDelayMilliseconds/WaitForNextRead timing, policy or placement;
- change WinRT ReportInterval or CG3EM/legacy COM implementation;
- add gyro deadzones, smoothing, bias calibration/subtraction, interpolation, motion fusion, quaternions, gyro-to-mouse transforms, Steam Input configuration, or app UI;
- change fresh thresholds (250ms gyro / 500ms accel), axis signs/scales, Steam Deck native input/ABI, XInput paths, publishers, or native output cadence;
- rework Full1902 power/ownership recovery, PID1901↔PID1902, HidHide, VIIPER, Center M, or rumble;
- add a diagnostics manager, background rescan/polling service, central logging authority, per-sample files, state machine, or speculative race-hardening.

The **next** PR, after the user's logs are observed, can evaluate reducing/removing redundant reader waits, handling stale timestamp age, and narrowly scoped bias/noise mitigation based on measured A2VM versus EX data. Do not fold any unmeasured fix into this diagnostics PR.

## 10. Validation and handoff

Local Codex runs and reports:

~~~powershell
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
~~~

For PR review, consider only reachable material regressions: reader stalls due to diagnostic work, altered polling or output, Debug logging flood, stale telemetry after restart, or broken teardown. Do not block for theoretical single-instruction races, new defensive abstractions, or physical-hardware validation. **Real-device verification is the USER's post-merge role; not a local Codex, CI, or PR-review merge gate.**

User post-merge collection suggestion (not a Codex requirement): enable Debug, keep A2VM or EX stationary for at least 20–30 seconds, then perform slow steady rotation for at least 20–30 seconds and test Steam Input gyro-as-mouse versus gyro-as-joystick. Preserve ControllerMotion summaries and existing SteamOutput heartbeats from the same session; report the physical model and Steam Input activation/smoothing configuration. Compare input-side FreshAcceptedCount/ActualFreshHz/gaps/stddev to publisher IMU validity/reuse and SetStateHz before any behavioral change.

**Deliverable:** one focused PR implementing Debug-only diagnostics, with exact field documentation and deterministic unit-test results; no independent application change outside gyro telemetry.