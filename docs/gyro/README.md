# Gyro / IMU Research Index

Status: research, diagnostic implementation, and design consolidation  
Last updated: 2026-09-05  
Production status: **Steam Deck IMU output is not implemented yet.**

This folder is the single documentation home for MSI Claw gyro / accelerometer research and the Addon's SD6 motion feature track.

## Current implementation state

The first two SD6-A diagnostic implementation steps are now on `main`:

```text
PR #494
→ Environment Discovery Windows motion / Sensor API evidence

PR #495
→ Claw Sensor Probe PR A
→ backend-aware discovery/readers/timing
→ schema v2 foundation
```

Current source baseline after PR #495:

```text
9ee8dd20a71e9434473afa842f8b3489c83adcac
```

The active next implementation step is **Claw Sensor Probe PR B**: capture-mode selection, Axis per-phase summaries, Stationary Bias summaries, and the developer UI/transport projection required to run those captures.

Real CG3EM hardware acceptance remains a separate post-merge validation step. Production SD6 motion output must not be implemented until the completed diagnostic has produced a measured CG3EM source contract.

## Current reading order

1. [`GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md`](./GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md)  
   Current synthesis. Start here. It reconciles the earlier CG3EM research, the 2026-08-15 driver correction, the current Full1902 / Steam Deck architecture, and the newer WSGM A2VM findings.

2. [`DEVELOPER_DISCOVERY_REPORT_GYRO_REVIEW_2026-09-05.md`](./DEVELOPER_DISCOVERY_REPORT_GYRO_REVIEW_2026-09-05.md)  
   Review of the Developer Menu **Environment Discovery Report** and the sensor/ISH evidence it should add before SD6.

3. [`DEVELOPER_CLAW_SENSOR_PROBE_GYRO_REVIEW_2026-09-05.md`](./DEVELOPER_CLAW_SENSOR_PROBE_GYRO_REVIEW_2026-09-05.md)  
   Review of the Developer Menu **Claw Sensor Probe / Gyro Test** and the concrete changes needed to characterize CG3EM correctly.

## Implementation work orders

Implementation history / order:

1. [`SD6A_ENVIRONMENT_DISCOVERY_SENSOR_EVIDENCE_WORK_ORDER.md`](./SD6A_ENVIRONMENT_DISCOVERY_SENSOR_EVIDENCE_WORK_ORDER.md) — **implemented by PR #494**  
   Extend the existing one-shot Environment Discovery Report with WinRT motion projection status, legacy Sensor API broad/direct-query evidence, exact HRESULTs, and a motion-focused view of the already-captured all-class PnP inventory.

2. [`SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md`](./SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md) — **overall SD6-A probe design / split plan**  
   Defines the complete backend-aware Claw Sensor Probe characterization target and the PR-A / PR-B split. Its PR-A portion was implemented by PR #495; use the dedicated PR-B work order below for the remaining current-code implementation.

3. [`SD6A_CLAW_SENSOR_PROBE_PR_B_CAPTURE_MODES_AND_SUMMARIES_WORK_ORDER.md`](./SD6A_CLAW_SENSOR_PROBE_PR_B_CAPTURE_MODES_AND_SUMMARIES_WORK_ORDER.md) — **active next work order**  
   Prepared against `main` after PR #495. Adds Live Sanity / Axis Characterization / Stationary Bias modes through the existing Start RPC, completes schema-v2 per-phase and bias summaries, exposes compact PR-A timing/backend evidence to the developer UI, preserves the existing 200 ms polling and Runtime-owned session lifecycle, and explicitly keeps CG3EM hardware acceptance as post-merge validation.

## Historical / source documents

- [`Reference Research_CG3EM Gyro Driver Correction_2026-08-15.txt`](./Reference%20Research_CG3EM%20Gyro%20Driver%20Correction_2026-08-15.txt)  
  Existing repository erratum moved here from `docs/`. Driver reinstallation restored Windows sensor access on the tested CG3EM system. Exact CG3EM sensor identities, units, axes, rates, and lifecycle behavior still require a fresh capture before production SD6 work.

The earlier `gyro.txt` research notes and the detailed `SteamInputAddonforClaw_CG3EM_Gyro_Reanalysis_2026-08-12` source material were used when producing the consolidated design above. They predate the current Full1902 Steam Deck architecture, and the 2026-08-12 hardware-level interpretation is superseded by the 2026-08-15 correction. The consolidated design preserves the still-valid conclusions and explicitly marks the superseded ones rather than treating those early notes as current implementation contracts.

## Current authority hierarchy

When documents disagree, use this order:

1. current repository code and Full1902 architecture;
2. the active implementation work order for the current PR;
3. `GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md`;
4. the 2026-08-15 CG3EM driver correction;
5. earlier CG3EM / Classic Steam Controller research only as historical provenance.

External WSGM A2VM findings are useful **reference evidence**, not CG3EM hardware evidence. In particular, do not copy A2VM sensor GUIDs, device paths, axis transforms, bias values, or freshness thresholds into CG3EM production code until the Addon's own CG3EM diagnostic capture confirms them.
