# Work Order — Gyro PR3: Match the Proven Claw Sensor Probe Polling Policy in Production

**Repository:** `onehoon/SteamAddonforClaw`  
**Scope:** One focused local-Codex PR; A2VM 7, A2VM 8, and EX/CG3EM; gyro **and** accelerometer  
**Design decision:** Production already uses the same low-level sensor source handles as Claw Sensor Probe. Align only the *per-read waiting policy*; do not run a diagnostic Probe session in production or introduce another reader architecture.  
**Implementation owner:** Local Codex (code, automated tests, PR). **Physical device validation owner:** User, **after merge**. Hardware verification is not required for implementation, CI, review approval, or merge.

## 1. Problem / measured baseline

PR #749 introduced Debug-only production sensor cadence diagnostics and has been merged. The A2VM 8-inch `0.1.359.0` BPM run exposes a substantial gap between the older, independent sensor Probe measurements and the production sensor-reading rate:

| Sensor | Earlier independent Probe evidence | Production 359 BPM stable 100-second window |
| --- | ---: | ---: |
| A2VM 8 WinRT gyroscope | Approximately **100 fresh samples/s** | **37.10 fresh samples/s**; mean accepted gap **26.96 ms** |
| A2VM 8 legacy accelerometer | Roughly **300 fresh samples/s** | **64.61 fresh samples/s**; mean accepted gap **15.48 ms** |
| EX/CG3EM legacy gyroscope | **98.2–98.7 fresh samples/s** | Not yet measured with PR #749 production diagnostics on EX |
| EX/CG3EM legacy accelerometer | **273–276 fresh samples/s** | Not yet measured with PR #749 production diagnostics on EX |

A2VM 359 production configuration was WinRT gyro `Minimum/Requested/EffectiveReportIntervalMs=10`, plus **an additional** `PollDelayMs=10` after *every* read; legacy accel used an additional 2 ms. Production accepted all observed nonempty sensor readings (0 duplicate-timestamp and 0 no-data counts), but only received ~37 gyro and ~65 accel fresh reports/s in the steady window. The virtual Steam Deck publisher remained ~250 Hz, with zero SetState failures and ~85.1% gyro receive-timestamp reuse. It therefore cannot be used as a proxy for physical sensor Hz.

This is a **confirmed production-vs-Probe acquisition-policy mismatch**. It is **not yet proof of the hardware sensor's absolute maximum output data rate** or proof that removing the wait alone will reach the Probe rates. The reported targets are empirical Windows sensor API acquisition baselines, not chip datasheet ODRs or hard acceptance thresholds.

Evidence:

- [A2VM 359 BPM runtime log (2026-10-11 15:40 session)](https://drive.google.com/file/d/1jO4ObgyFFQSuzK825gfYfz68GrvUXHAx/view)
- [A2VM 2026-10-09 Probe capture folder](https://drive.google.com/drive/folders/1hzsKL82VmSPfJYls6zY4Kn2gmADTT6Ki)
- [Full1902 Gyro PR1/PR2 implementation plan](../gyro/FULL1902_GYRO_PR1_PR2_IMPLEMENTATION_PLAN_2026-10-09.md)
- [CG3EM live-sanity hardware measurements](../gyro/CG3EM_LIVE_SANITY_HARDWARE_RESULTS_2026-09-05.md), including real sporadic ~120–141 ms legacy gyro blocking reads.

**Goal:** Give production the *same minimal wait policy* as the already-working Claw Sensor Probe, preserving all production ownership, native publication, timestamp/freshness handling, and Debug telemetry.

## 2. Required architectural context / precedence

Read before editing:

1. [Full1902 document authority](../Full%201902%20Implementation/README.md) and applicable authority documents in that folder. This is a **standalone Full1902 application**; legacy CTW integration text is out of scope.
2. [Gyro PR1 production Motion Core work order](GYRO_PR1_FULL1902_COMMON_MOTION_CORE_WORK_ORDER_2026-10-09.md).
3. [Gyro PR2 native IMU publication work order](GYRO_PR2_FULL1902_STEAMDECK_NATIVE_IMU_OUTPUT_WORK_ORDER_2026-10-09.md).
4. [PR #749 Debug cadence diagnostics work order](GYRO_DEBUG_SENSOR_CADENCE_JITTER_DIAGNOSTICS_WORK_ORDER_2026-10-11.md). This PR3 deliberately changes the polling rule that PR #749 instrumented, while retaining its diagnostics.
5. Actual current `main` sources/tests, not stale branch line numbers.

Supported deployment is one interactive Windows user/session. Do not introduce cross-session authorities or abstractions to handle unsupported Fast User Switching/RDP.

## 3. Verified current code paths

| File | Responsibility / existing behavior |
| --- | --- |
| `src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeWinRtSources.cs` | Common `IClawSensorProbeSourceHandle.Read()` and `Configuration`. WinRT `Gyrometer.GetCurrentReading()`, `ReportInterval` negotiation; legacy Sensor API `ReadXYZ` via `ClawSensorProbeLegacySourceHandle`. Both Probe and production **already reuse these implementations**. |
| `src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeReaders.cs` | Probe read loop: `handle.Read()`; **`Thread.Sleep(1)` only for NoData or duplicate readings**; on accepted fresh readings, observe/report and immediately attempt the next read without an additional programmed sleep. |
| `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawMotionSource.cs` | Production `ReadRoleAsync`: calls the same `handle.Read()`, classifies NoData and duplicate timestamps, publishes normalized motion snapshot, then **always calls `WaitForNextRead(cancellationToken, pollDelayMs)`** on accepted, duplicate, and NoData paths. `PollDelayMilliseconds(candidate)` currently returns the candidate minimum report interval or default 10 ms. |
| `src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawMotionState.cs` | Existing immutable gyro/accel snapshot and monotonic freshness authority. |
| `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs` | Existing independent ~250 Hz virtual Steam Deck publisher, unchanged by this PR. |
| `tests/SteamInputAddonforClaw.Tests/MsiClawMotionSourceTests.cs` | Fake source handles, blocked-read, role isolation, startup/teardown/restart and Full1902 lifecycle contracts. |
| `tests/SteamInputAddonforClaw.Tests/MsiClawMotionDiagnosticsTests.cs` | PR #749 Debug event/configuration/counter contracts, including a test currently asserting the old `PollDelayMeaning=ExtraApplicationWaitAfterEachRead`. |

**Do not redirect production through `ClawSensorProbeReaders` or `ClawSensorProbeCoordinator`.** Those are UI/CSV/report/capture-session owners with a separate session lifecycle. The shared low-level read path is already correct. Only the production wait policy is different.

## 4. Exact implementation

### 4.1 Match Probe's waiting policy for both source roles on all supported models

Make the smallest possible edit in `MsiClawMotionSource.ReadRoleAsync`:

1. **Fresh/accepted sample:** after current `PublishSample(...)` and existing optional Debug accumulation, perform **no explicit sensor polling delay**. Keep the existing due-summary diagnostic call, then continue to the next `handle.Read()` on that role's current reader thread.
2. **NoData:** preserve classification and telemetry; apply a **1 ms cancellation-aware backoff**, then retain the due-summary diagnostic call and continue.
3. **Duplicate sensor timestamp:** preserve the existing duplicate rule, classification and telemetry; apply the same **1 ms cancellation-aware backoff**, retain due-summary diagnostic call and continue.
4. **Failed `Read()`, rejected nonfinite sample or `PublishSample` returning false:** leave current production failure/return semantics exactly as they are. Do not convert a reader error into a polling retry.
5. **Cancellation:** retain the existing before-publish cancellation check, existing source invalidation and bounded reader shutdown policy. The 1 ms waits must remain cancellation-aware rather than using unconditional `Thread.Sleep(1)` as in the diagnostic-only Probe.

Illustrative control flow only (retain the original log, diagnostic, cancellation and error branches around these lines):

```csharp
if (!sample.HasData)
{
    if (recordThisRead)
        diagnostics.ObserveNoData();

    WaitForNextRead(cancellationToken, 1);
    EmitReaderDiagnosticsIfDue(/* existing arguments */);
    continue;
}

DateTimeOffset? sensorTimestamp =
    sample.SensorTimestamp == default ? null : sample.SensorTimestamp;
if (IsDuplicateSensorTimestamp(previousSensorTimestamp, sensorTimestamp))
{
    if (recordThisRead)
        diagnostics.ObserveDuplicateTimestamp();

    WaitForNextRead(cancellationToken, 1);
    EmitReaderDiagnosticsIfDue(/* existing arguments */);
    continue;
}

// Preserve timestamp assignment, PublishSample, Debug ObserveFresh,
// acceptance/cancellation safety and existing code structure.
// No WaitForNextRead after a successfully accepted fresh sample.
EmitReaderDiagnosticsIfDue(/* existing arguments */);
```

`1 ms` is the Probe's **non-fresh backoff**, not a target sensor period or a request to set Windows gyro's `ReportInterval` to 1 ms. Do not busy-spin on duplicate timestamps or NoData. Do not force the virtual Steam Deck, DirectInput or any physical sensor to 250 Hz. Keep A2VM's existing WinRT report-interval configuration and EX's legacy backend unchanged.

If a small constant for the 1 ms backoff makes intent clearer, define it locally; do **not** add a configurable scheduler, sampling-mode policy, event subscription system, second reader, new lock, new state machine or new sensor owner. Remove `PollDelayMilliseconds(candidate)` if no longer used; do not leave unused interval-as-sleep policy in the file.

### 4.2 Correct the PR #749 diagnostic meaning without losing the evidence

PR #749 logs `MotionReaderDiagnosticsStarted` and `MotionReaderDiagnosticsSummary` with `PollDelayMs`, and the start record explicitly says `PollDelayMeaning=ExtraApplicationWaitAfterEachRead`. That will be **false** after PR3.

- Preserve **all existing Debug diagnostics** (actual fresh Hz, read duration, fresh receive gaps, sensor timestamp deltas/ages, samples, duplicates, NoData, standard deviation), unchanged in frequency and measurement meaning.
- Keep `CandidateMinimumReportIntervalMs` and WinRT `Minimum/Requested/EffectiveReportIntervalMs` as physical/backend configuration evidence, **not a software sleep policy**.
- Update the delay fields to describe reality: e.g. `PollDelayMs=1` and `PollDelayMeaning=OnlyAfterNoDataOrDuplicate`, or clearer `NonFreshBackoffMs=1` plus `FreshAcceptedExtraWaitMs=0`. Choose one concise coherent field contract; use the same policy in both start and summary records.
- Update tests asserting the prior delay meaning. Preserve the 10-second aggregation cadence, Debug-only gating, safe reset/restart behavior and existing publisher-side `MotionImuPublishDiagnosticsSummary`.
- Do not create per-read logging, a new timer, or an independent telemetry system just to compare rates.

### 4.3 Leave everything else alone

Do not change:

- Source discovery/selection, A2VM 7/8 WinRT gyro vs legacy accel, or EX/CG3EM dual legacy readers.
- Sensor handle creation/configuration/release, legacy COM ownership or WinRT ReportInterval negotiation/reset.
- `MsiClawMotionState` normalization, axis mapping, receive clocks, timestamp deduplication, independent gyro/accel validity/freshness or rejection semantics.
- Steam Deck IMU conversion/packing, `CanonicalSteamDeckInputPublisher` 4 ms / ~250 Hz cadence, VIIPER/native mapper, output failure policy, or downstream Steam Input mapping.
- Full1902 controller state/routing, HidHide ownership, PID1901↔PID1902 restoration, device re-enumeration/recovery/fail-close, suspend/hibernate/resume, startup/shutdown, Center M control or rumble.
- Diagnostic Probe behavior, UI, CSV, capture modes, or its lifecycle.

Any code touched outside the production source and directly associated tests needs a concrete functional justification in the PR.

## 5. Tests and code review acceptance (automated, no hardware prerequisite)

Add/update focused deterministic tests using the repository's existing fake source handles and test patterns:

1. **Accepted fresh path:** for both gyro and accelerometer, a stream of valid advancing-timestamp samples is repeatedly read/published without applying the old candidate-minimum (`10 ms` gyro/`2 ms` accel) wait. Confirm no extra delay is scheduled on the successful fresh branch. Avoid brittle stopwatch-speed assertions or dependence on real OS scheduling.
2. **Duplicate path:** an unchanged valid timestamp is not published as a new physical sample; a cancellation-aware 1 ms backoff applies before another read. Existing debug duplicate counts remain accurate.
3. **NoData path:** a NoData response produces no false fresh value and gets the same non-fresh backoff; Debug telemetry still counts it.
4. **Multiple models/backends:** A2VM 7, A2VM 8 and CG3EM all use the **same** per-outcome policy while retaining their original source selection and backend behavior. Do not assert that simulated sources must reach an exact Hz.
5. **Diagnostics regression:** update the previously hardcoded `PollDelayMeaning=ExtraApplicationWaitAfterEachRead` test. Verify the new delay meaning and retain existing Debug-only summaries, correct sensor frequency/age/duplicate classification, source configuration and output-side counters. Do not assert 100 Hz or 300 Hz against fake sensors.
6. **Actual lifecycle safety:** a potentially blocking legacy read does not block the controller/Steam publisher; source cancellation, blocked-read timeout/deferred release, physical loss, suspend/resume and stop/restart retain existing behavior and do not leak or carry reader generations.
7. **No unrelated controller regressions:** existing normal and BPM Steam Deck acquisition guards, 250 Hz publisher, and native IMU mapping tests still pass.

Prefer extending `MsiClawMotionSourceTests.cs` and `MsiClawMotionDiagnosticsTests.cs` instead of adding test-only production interfaces/managers. A very small test seam is acceptable **only if actually needed** to test the wait decision without sleep-based timing. Do not build extra synchronization or complexity for artificial instruction-interleaving races.

Run appropriate `dotnet build` and relevant solution/unit tests, report results, and keep `git diff --check` clean. Ordinary code/build/test defects can block review; **post-merge device measurements cannot**.

## 6. User-owned post-merge physical verification (informational; NOT a merge gate)

The user will install the resulting build on supported hardware, enable Debug, enter Steam BPM gyro calibration (no game required for source acquisition), and compare the exact same session profile to build `0.1.359.0`:

- A2VM 8: check whether gyro rises materially from **37.1 Hz toward the existing independent ~100 Hz Probe observation**, and accel from **64.6 Hz toward ~300 Hz**; inspect `ReadAttempts`, `FreshAcceptedCount`, duplicate/no-data counts, `ReadDurationMeanMs`, receive-gap mean/max, timestamp intervals/age and long-gap bins.
- EX/CG3EM: compare production with earlier **~98 Hz gyro / ~273–276 Hz accel** Probe measurements; preserve visibility into occasional 120–141 ms legacy blocking reads, rather than claiming they were fixed by polling policy.
- Verify output heartbeat still ~250 Hz, `SetStateFailures=0`, IMU usable vs unavailable/reused ticks, startup/restart and ordinary sleep/resume as practical.
- If actual fresh Hz does **not** improve, examine new duplicate/no-data/read-duration evidence **before** adding an event-driven source, explicit scheduler tuning, new polling modes or smoothing.
- If observed CPU cost becomes problematic, use measured evidence to decide on a minimal targeted adjustment in a separate follow-up PR.

## 7. Completion definition

One PR implementing only Probe-equivalent **outcome-dependent read-loop waiting** in the existing common production motion source, applicable to both sensor roles across A2VM 7/8 and EX/CG3EM; PR #749 diagnostic delay fields corrected; deterministic tests/build green; no changes to native output, controller ownership, other device features or sensor hardware configuration. Physical acceptance belongs to the user after merge.
