# Gyro / IMU Research Index

Status: research and design consolidation  
Last updated: 2026-09-05  
Production status: **Steam Deck IMU output is not implemented yet.**

This folder is the single documentation home for MSI Claw gyro / accelerometer research and the Addon's SD6 motion feature track.

## Current reading order

1. [`GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md`](./GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md)  
   Current synthesis. Start here. It reconciles the earlier CG3EM research, the 2026-08-15 driver correction, the current Full1902 / Steam Deck architecture, and the newer WSGM A2VM findings.

2. [`DEVELOPER_DISCOVERY_REPORT_GYRO_REVIEW_2026-09-05.md`](./DEVELOPER_DISCOVERY_REPORT_GYRO_REVIEW_2026-09-05.md)  
   Review of the Developer Menu **Environment Discovery Report** and the sensor/ISH evidence it should add before SD6.

3. [`DEVELOPER_CLAW_SENSOR_PROBE_GYRO_REVIEW_2026-09-05.md`](./DEVELOPER_CLAW_SENSOR_PROBE_GYRO_REVIEW_2026-09-05.md)  
   Review of the Developer Menu **Claw Sensor Probe / Gyro Test** and the concrete changes needed to characterize CG3EM correctly.

## Historical / source documents

- [`Reference Research_CG3EM Gyro Driver Correction_2026-08-15.txt`](./Reference%20Research_CG3EM%20Gyro%20Driver%20Correction_2026-08-15.txt)  
  Existing repository erratum moved here from `docs/`. Driver reinstallation restored Windows sensor access on the tested CG3EM system. Exact CG3EM sensor identities, units, axes, rates, and lifecycle behavior still require a fresh capture before production SD6 work.

The earlier `gyro.txt` research notes and the detailed `SteamInputAddonforClaw_CG3EM_Gyro_Reanalysis_2026-08-12` source material were used when producing the consolidated design above. They predate the current Full1902 Steam Deck architecture, and the 2026-08-12 hardware-level interpretation is superseded by the 2026-08-15 correction. The consolidated design preserves the still-valid conclusions and explicitly marks the superseded ones rather than treating those early notes as current implementation contracts.

## Current authority hierarchy

When documents disagree, use this order:

1. current repository code and Full1902 architecture;
2. `GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md`;
3. the 2026-08-15 CG3EM driver correction;
4. earlier CG3EM / Classic Steam Controller research only as historical provenance.

External WSGM A2VM findings are useful **reference evidence**, not CG3EM hardware evidence. In particular, do not copy A2VM sensor GUIDs, device paths, axis transforms, bias values, or freshness thresholds into CG3EM production code until the Addon's own CG3EM diagnostic capture confirms them.
