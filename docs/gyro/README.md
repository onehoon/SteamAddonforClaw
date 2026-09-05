# Gyro / IMU Research Index

Status: research, diagnostic implementation, and hardware characterization  
Last updated: 2026-09-05  
Production status: **Steam Deck IMU output is not implemented yet.**

This folder is the single documentation home for MSI Claw gyro / accelerometer research and the Addon's SD6 motion feature track.

## Current implementation state

The SD6-A diagnostic path is now implemented on `main`:

```text
PR #494
→ Environment Discovery Windows motion / Sensor API evidence

PR #495
→ Claw Sensor Probe PR A
→ backend-aware discovery/readers/timing
→ schema v2 foundation

PR #497
→ Claw Sensor Probe PR B
→ Live Sanity / Axis Characterization / Stationary Bias
→ summaries + developer UI/transport projection

PR #500
→ CG3EM duplicate legacy sensor-selection hotfix
→ logical SensorId dedupe
→ role-aware direct Physical Accelerometer selection
```

Current production-code checkpoint after PR #500:

```text
b1ff0b79dbd6a8215866567d900ee4499d262ec6
```

The active next step is **real CG3EM hardware characterization**, not production Steam Deck IMU implementation.

Two successful CG3EM Live Sanity captures have now confirmed that the repaired-driver machine exposes continuously readable STMicro LSM6DSO Physical Gyrometer and Physical Accelerometer sources through the legacy Windows Sensor API. The remaining characterization work is dedicated Stationary Bias, Axis Characterization, and practical restart / sleep-resume evidence before the measured CG3EM production source contract is frozen.

## Current reading order

1. [`GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md`](./GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md)  
   Current synthesis and SD6 architecture. It reconciles the earlier CG3EM research, the 2026-08-15 driver correction, the current Full1902 / Steam Deck architecture, and the newer WSGM A2VM findings.

2. [`CG3EM_LIVE_SANITY_HARDWARE_RESULTS_2026-09-05.md`](./CG3EM_LIVE_SANITY_HARDWARE_RESULTS_2026-09-05.md)  
   First real CG3EM post-PR-500 hardware checkpoint. Compares an approximately 60° stationary capture with a desk-laid stationary capture; records measured source selection, cadence, gravity vectors, repeatable gyro zero-rate center, and the reproduced ~120 ms legacy gyroscope blocking-read behavior.

3. [`DEVELOPER_DISCOVERY_REPORT_GYRO_REVIEW_2026-09-05.md`](./DEVELOPER_DISCOVERY_REPORT_GYRO_REVIEW_2026-09-05.md)  
   Review of the Developer Menu **Environment Discovery Report** and the sensor/ISH evidence added before SD6 characterization.

4. [`DEVELOPER_CLAW_SENSOR_PROBE_GYRO_REVIEW_2026-09-05.md`](./DEVELOPER_CLAW_SENSOR_PROBE_GYRO_REVIEW_2026-09-05.md)  
   Review of the Developer Menu **Claw Sensor Probe / Gyro Test** and the concrete changes required to characterize CG3EM correctly.

## Implementation work orders

Implementation history / order:

1. [`SD6A_ENVIRONMENT_DISCOVERY_SENSOR_EVIDENCE_WORK_ORDER.md`](./SD6A_ENVIRONMENT_DISCOVERY_SENSOR_EVIDENCE_WORK_ORDER.md) — **implemented by PR #494**  
   Extended the existing one-shot Environment Discovery Report with WinRT motion projection status, legacy Sensor API broad/direct-query evidence, exact HRESULTs, and a motion-focused view of the already-captured all-class PnP inventory.

2. [`SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md`](./SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md) — **overall SD6-A probe design / split plan**  
   Defines the complete backend-aware Claw Sensor Probe characterization target and the PR-A / PR-B split. PR A was implemented by #495 and PR B by #497.

3. [`SD6A_CLAW_SENSOR_PROBE_PR_B_CAPTURE_MODES_AND_SUMMARIES_WORK_ORDER.md`](./SD6A_CLAW_SENSOR_PROBE_PR_B_CAPTURE_MODES_AND_SUMMARIES_WORK_ORDER.md) — **implemented by PR #497**  
   Added Live Sanity / Axis Characterization / Stationary Bias modes through the existing Start RPC, completed schema-v2 per-phase and bias summaries, exposed compact timing/backend evidence to the developer UI, and preserved Runtime-owned session lifecycle.

The CG3EM duplicate-source selection failure discovered during the first physical Live Sanity attempt was subsequently fixed by PR #500 under:

```text
docs/work-order/SD6A_CG3EM_SENSOR_SELECTION_DUPLICATE_HOTFIX_WORK_ORDER.md
```

## Current hardware checkpoint

The two successful Live Sanity captures documented in `CG3EM_LIVE_SANITY_HARDWARE_RESULTS_2026-09-05.md` establish the following practical observations on the tested CG3EM unit:

```text
Physical Gyrometer     = Legacy Sensor API / STMicro LSM6DSO / ~100 Hz
Physical Accelerometer = Legacy Sensor API / STMicro LSM6DSO / ~275 Hz

Accelerometer magnitude ≈ 1.01 in two stationary postures
Gyro stationary center  ≈ raw (+0.41, -0.29, -0.18)
Gyro long read           ≈ isolated ~120 ms events, reproduced in both captures
```

These values are hardware evidence, not yet production constants.

Before production SD6 implementation, still collect:

```text
Stationary Bias — desk posture
Stationary Bias — materially tilted posture
Axis Characterization — 7 phases
Runtime/app restart -> fresh Live Sanity
Sleep/Resume        -> fresh Live Sanity
```

Then freeze the measured CG3EM source/units/axis/bias/staleness contract.

## Historical / source documents

- [`Reference Research_CG3EM Gyro Driver Correction_2026-08-15.txt`](./Reference%20Research_CG3EM%20Gyro%20Driver%20Correction_2026-08-15.txt)  
  Existing repository erratum moved here from `docs/`. Driver reinstallation restored Windows sensor access on the tested CG3EM system. The fresh September captures now supersede the earlier uncertainty about whether repaired-driver CG3EM exposes usable host-readable motion sensors, while axis/sign, final unit declaration, bias policy, and lifecycle behavior still require the remaining characterization.

The earlier `gyro.txt` research notes and the detailed `SteamInputAddonforClaw_CG3EM_Gyro_Reanalysis_2026-08-12` source material were used when producing the consolidated design above. They predate the current Full1902 Steam Deck architecture, and the 2026-08-12 hardware-level interpretation is superseded by the 2026-08-15 correction and the newer September CG3EM physical captures. The consolidated design preserves the still-valid conclusions and explicitly marks the superseded ones rather than treating those early notes as current implementation contracts.

## Current authority hierarchy

When documents disagree, use this order:

1. current repository code and Full1902 architecture;
2. measured current-device hardware evidence such as `CG3EM_LIVE_SANITY_HARDWARE_RESULTS_2026-09-05.md` for facts actually established by those captures;
3. the active implementation work order for the current PR;
4. `GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md`;
5. the 2026-08-15 CG3EM driver correction;
6. earlier CG3EM / Classic Steam Controller research only as historical provenance.

External WSGM A2VM findings are useful **reference evidence**, not CG3EM hardware evidence. In particular, do not copy A2VM sensor GUIDs, device paths, axis transforms, bias values, or freshness thresholds into CG3EM production code until the Addon's own CG3EM diagnostic captures confirm them.