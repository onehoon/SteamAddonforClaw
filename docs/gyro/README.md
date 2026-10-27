# Gyro / IMU Research Index

Status: research, diagnostic implementation, and hardware characterization  
Last updated: 2026-10-11  
Production status: **Steam Deck native gyro output present in Addon 0.1.365; production gyro polling improved; Deck gyro-to-mouse choppiness remains unresolved and its investigation is deferred.** Historical PR milestones below reflect the state when originally written.

This retained index summarizes the Addon's SD6 motion feature track and links to detailed research and hardware reports preserved in Google Drive.

## Deferred Steam Deck gyro-to-mouse choppiness investigation (2026-10-11)

- [Full1902 Steam Deck Gyro Choppiness — Investigation and Deferred Handoff](https://drive.google.com/file/d/1BHlqVv9c5_evEc-gxdDzk9AcWGMqYFzy/view?usp=drivesdk) — collected 0.1.359/0.1.360/0.1.365 sensor and publisher evidence, VIIPER PR #59 outcome, Classic Steam Controller vs Deck SDL/Linux comparison, HHC SteamDeckTarget reference, confirmed sporadic stalls, remaining unknown Windows/Steam Input host-consumption boundary, and the bounded deferred diagnostic plan. **No further implementation approved by this report; hardware checks remain user-owned after merge.**

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

The shared production Motion Core for A2VM and CG3EM was merged in **PR #731** (squash commit `a1a57ed8cdb78d05e016f862f22637ae446bd58c`). **Gyro PR2 Steam Deck native IMU publication is implemented in the current review PR**, using that PR1 snapshot without a new sensor owner, new virtual publisher or Addon gyro UI. EX-specific hardware confirmation and A2VM7 physical validation remain **post-merge user responsibilities**, not Codex/CI/PR-review blockers. The PR milestones above record the implementation sequence.

Two successful CG3EM Live Sanity captures have now confirmed that the repaired-driver machine exposes continuously readable STMicro LSM6DSO Physical Gyrometer and Physical Accelerometer sources through the legacy Windows Sensor API. The remaining characterization work is dedicated Stationary Bias, Axis Characterization, and practical restart / sleep-resume evidence before the measured CG3EM production source contract is frozen.

## Current reading order

1. [GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md](https://drive.google.com/file/d/1lxHUe42eYSyqbnCA-7kLjHiQ4WT3Zf1t/view?usp=drivesdk)<br>
   Current synthesis and SD6 architecture. It reconciles the earlier CG3EM research, the 2026-08-15 driver correction, the current Full1902 / Steam Deck architecture, and the newer WSGM A2VM findings.

2. [CG3EM_LIVE_SANITY_HARDWARE_RESULTS_2026-09-05.md](https://drive.google.com/file/d/1QPeVm-2w-5IrNnIin29hwdgSkq27QZx6/view?usp=drivesdk)<br>
   First real CG3EM post-PR-500 hardware checkpoint. Compares an approximately 60° stationary capture with a desk-laid stationary capture; records measured source selection, cadence, gravity vectors, repeatable gyro zero-rate center, and the reproduced ~120 ms legacy gyroscope blocking-read behavior.

3. [DEVELOPER_DISCOVERY_REPORT_GYRO_REVIEW_2026-09-05.md](https://drive.google.com/file/d/1ITlNHPEtdmzkmY2dDS0sXV0HvzOGxovy/view?usp=drivesdk)<br>
   Review of the Developer Menu **Environment Discovery Report** and the sensor/ISH evidence added before SD6 characterization.

4. [DEVELOPER_CLAW_SENSOR_PROBE_GYRO_REVIEW_2026-09-05.md](https://drive.google.com/file/d/1Z1USet-qgNJn8zGwW5pszg159Azb0APE/view?usp=drivesdk)<br>
   Review of the Developer Menu **Claw Sensor Probe / Gyro Test** and the concrete changes required to characterize CG3EM correctly.

## Current hardware checkpoint

The two successful Live Sanity captures documented in [CG3EM_LIVE_SANITY_HARDWARE_RESULTS_2026-09-05.md](https://drive.google.com/file/d/1QPeVm-2w-5IrNnIin29hwdgSkq27QZx6/view?usp=drivesdk) establish the following practical observations on the tested CG3EM unit:

```text
Physical Gyrometer     = Legacy Sensor API / STMicro LSM6DSO / ~100 Hz
Physical Accelerometer = Legacy Sensor API / STMicro LSM6DSO / ~275 Hz

Accelerometer magnitude ≈ 1.01 in two stationary postures
Gyro stationary center  ≈ raw (+0.41, -0.29, -0.18)
Gyro long read           ≈ isolated ~120 ms events, reproduced in both captures
```

These values are hardware evidence, not yet production constants.

For user **post-merge hardware confirmation**, the historically recommended captures include:

```text
Stationary Bias — desk posture
Stationary Bias — materially tilted posture
Axis Characterization — 7 phases
Runtime/app restart -> fresh Live Sanity
Sleep/Resume        -> fresh Live Sanity
```

These are recommended physical validation captures and are **not** required before building, reviewing, or merging Gyro PR1/PR2; the implementation adopts explicit initial model policies based on the updated external references and measured evidence.

## Historical / source documents

- [Reference Research_CG3EM Gyro Driver Correction_2026-08-15.txt](https://drive.google.com/file/d/1YgEmtPBU2jQ2ch8NeHSXAGMyjIIUWJd6/view?usp=drivesdk)<br>
  Historical driver-correction reference preserved in Google Drive. Driver reinstallation restored Windows sensor access on the tested CG3EM system. The fresh September captures now supersede the earlier uncertainty about whether repaired-driver CG3EM exposes usable host-readable motion sensors, while axis/sign, final unit declaration, bias policy, and lifecycle behavior still require the remaining characterization.

The earlier `gyro.txt` research notes and the detailed `SteamInputAddonforClaw_CG3EM_Gyro_Reanalysis_2026-08-12` source material were used when producing the consolidated design above. They predate the current Full1902 Steam Deck architecture, and the 2026-08-12 hardware-level interpretation is superseded by the 2026-08-15 correction and the newer September CG3EM physical captures. The consolidated design preserves the still-valid conclusions and explicitly marks the superseded ones rather than treating those early notes as current implementation contracts.

## Current authority hierarchy

When documents disagree, use this order:

1. current repository code and Full1902 architecture;
2. measured current-device hardware evidence such as [CG3EM_LIVE_SANITY_HARDWARE_RESULTS_2026-09-05.md](https://drive.google.com/file/d/1QPeVm-2w-5IrNnIin29hwdgSkq27QZx6/view?usp=drivesdk) for facts actually established by those captures;
3. the current implementation and review state;
4. [GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md](https://drive.google.com/file/d/1lxHUe42eYSyqbnCA-7kLjHiQ4WT3Zf1t/view?usp=drivesdk);
5. [the 2026-08-15 CG3EM driver correction](https://drive.google.com/file/d/1YgEmtPBU2jQ2ch8NeHSXAGMyjIIUWJd6/view?usp=drivesdk);
6. earlier CG3EM / Classic Steam Controller research only as historical provenance.

External WSGM A2VM findings are useful **reference evidence**, not CG3EM hardware evidence. In particular, do not copy A2VM sensor GUIDs, device paths, axis transforms, bias values, or freshness thresholds into CG3EM production code until the Addon's own CG3EM diagnostic captures confirm them.
