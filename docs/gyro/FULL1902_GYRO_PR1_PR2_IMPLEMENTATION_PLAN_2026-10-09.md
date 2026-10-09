# Full1902 MSI Claw Motion / Steam Deck IMU — Implementation Plan

**Date:** 2026-10-09  
**Status:** Approved two-PR implementation direction; production behavior remains subject to post-merge user hardware validation  
**Project:** Standalone Steam Addon for Claw, Full PID1902 architecture  
**Targets:** MSI Claw 7 AI+ A2VM (`MS-1T42`), Claw 8 AI+ A2VM (`MS-1T52`), Claw 8 EX AI+ CG3EM (`MS-1T91`)

## 1. Approved scope and order

| Stage | Scope | A2VM | EX/CG3EM | Notes |
|---|---|---|---|---|
| **Gyro PR1** | One shared production motion-acquisition and normalization module, live immutable snapshots, freshness, recovery/teardown | Implement | Implement | **No virtual Steam Deck IMU publication** |
| **Gyro PR2** | Publish PR1 motion via the existing canonical VIIPER Steam Deck native IMU path | Implement | Implement | Steam Input owns activation, mapping and sensitivity |
| Later, separate decision | Xbox360 gyro-to-right-stick (and any optional mouse/UI/profile work) | Deferred | Deferred | Xbox360/XInput has no native gyro fields |

**Implement both model families in PR1 and PR2, then perform actual-hardware validation later.** No PR1/PR2 code review or CI requirement may demand device-in-hand verification. Local Codex writes and statically tests code; the user owns post-merge device validation.

No CTW integration, CTW IPC, CTW process, or CTW dependency is permitted. HHC and CTW Dev are **reference implementations for physical IMU behavior**, not runtime dependencies.

## 2. Existing production authority to preserve

Read together:
- [Full1902 document authority](../Full%201902%20Implementation/README.md)
- [Full1902 controller architecture](../Full%201902%20Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md)
- [Elevated Runtime architecture](../Full%201902%20Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md)
- [Previous SD6 research/design](GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md) — its 2026-09-05 *pending CG3EM research* status is historical, superseded for the agreed implementation sequence by this plan.
- [CG3EM September live sanity evidence](CG3EM_LIVE_SANITY_HARDWARE_RESULTS_2026-09-05.md)

Full1902 contracts are not changed:

```text
Exact Center M Disabled / supported MSI Claw / admitted Full1902 physical owner
  -> one owned PID1902 DirectInput session
  -> persistent HidHide isolation
  -> one canonical VIIPER server/bus, two persistent typed devices
  -> Steam/BPM inactive: Xbox360 publisher
  -> Steam/BPM active: SteamDeck publisher
```

The gyro feature is a **subordinate motion capability**, not another controller, ownership mode, HidHide client, VIIPER publisher, route decision or authority. It must not alter PID1901/PID1902 restoration, rumble, WinG suppression, QAM, controller buttons/triggers/sticks, or normal route-switch behavior.

Physical motion acquisition remains active and independent of the **currently attached virtual presentation** when Full1902 physical ownership is active. This lets future Xbox360 motion consume the same snapshots without starting a second sensor service.

## 3. Concrete hardware and reference evidence

### 3.1 A2VM (direct capture on 2026-10-09)

Original user-hosted evidence:
- [A2VM probe logs folder](https://drive.google.com/drive/folders/1hzsKL82VmSPfJYls6zY4Kn2gmADTT6Ki)
- [Axis characterization session 20261009-112331](https://drive.google.com/drive/folders/1DP_-SKNgGiMtOvr4tiPQfWnZN6bwHrgD)
- [Axis characterization session 20261009-112310](https://drive.google.com/drive/folders/1SBD6T_5LKOW9MYRvOB156Zz30yJAizrA)

Observed on 8-inch A2VM (`msi.claw.a2vm.8`, `MS-1T52`):
- Gyro: WinRT `Gyrometer`, live `AngularVelocityX/Y/Z`, **degrees/second**, approximately 100 Hz.
- Accel: legacy Windows Sensor API `Physical Accelerometer` using STMicro LSM6DSO custom type `E83AF229-8640-4D18-A213-E22675EBB2C3` and data FMTID `B14C764F-07CF-41E8-9D82-EBE3D0776A6F` / property IDs 7, 8, 9; observed approximately 1g stationary, sample rates roughly 300 Hz.
- WinRT Accelerometer was unavailable in the captured installation.
- Gyro physical-axis characterization: Raw X responds to pitch, Raw Y to roll, Raw Z to yaw, consistently with the HHC/CTW logical gyro axis reordering.
- Additional supported A2VM 7-inch model uses an exact board ID but has not been separately measured with this October dataset; implement its model branch and treat hardware validation as pending.

### 3.2 EX / CG3EM (direct capture 2026-09-05)

- Board `MS-1T91`, Addon model `msi.claw.cg3em`.
- Gyro and accel: **legacy Windows Sensor API** `Physical Gyrometer` + `Physical Accelerometer`, distinct logical sensor IDs, custom type GUID as above, each using separate report fields PID 7/8/9.
- Actual rates: gyro approximately 98 Hz; accel approximately 273–276 Hz.
- Real blocking gyro reads of approximately 120–141 ms occurred even with no read errors. Sensor access **must never block the 250 Hz physical DirectInput / virtual-publisher hot paths**.
- Accelerometer stationary vector has magnitude approximately 1g in distinct tilts; the probe's `UnitBasis=Unknown` refers to lack of a formally declared legacy units contract, not evidence of an unrelated arbitrary unit.
- Axis orientation/sign and final Steam Deck target response **have not been physically verified on EX**. Nevertheless implement the common reference transform in PR1/PR2 and record this limitation; post-merge user validation follows.

### 3.3 Reference cross-check: HHC vs CTW Dev

Sources:
- [HHC ClawA2VM.json](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Resources/Devices/ClawA2VM.json)
- [HHC ClawCG3EM.json](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Resources/Devices/ClawCG3EM.json)
- [HHC SteamDeckTarget.cs](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Targets/SteamDeckTarget.cs)
- [HHC legacy Windows gyrometer](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Sensors/IMUWindowsGyrometer.cs)
- [CTW Dev ClawGyroSourceAdapter.cs](https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/XboxGamingBarHelper/ControllerEmulation/ClawGyroSourceAdapter.cs)
- [CTW Dev SensorApiGyroSourceAdapter.cs](https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/XboxGamingBarHelper/ControllerEmulation/SensorApiGyroSourceAdapter.cs)

For **both supported current Claw families**, current HHC configuration and CTW Dev agree on the gyro application-space transform:

```text
NormalizedGyroX = RawGyroX
NormalizedGyroY = RawGyroZ
NormalizedGyroZ = -RawGyroY
unit: deg/s
```

**Accelerometer disagreement:** HHC's *current A2VM and CG3EM model files* specify `(X,Z,-Y)`, while CTW Dev still uses the older ClawA1M reference `(-X,-Z,Y)`. The direct stationary A2VM measurements establish gravity magnitude/direction in **sensor coordinates**, but alone cannot settle competing application-space conventions. Adopt the current HHC model files as the *initial* common normalization, with explicit user hardware validation of target orientation later:

```text
NormalizedAccelX = RawAccelX
NormalizedAccelY = RawAccelZ
NormalizedAccelZ = -RawAccelY
unit: g
```

Do not implement two competing accelerometer coordinate pipelines, nor silently claim CTW Dev is equivalent. The current CTW Dev `BuildGyroSourceAdapter` branch returns `null` for MSI Claw (temporary gyro disable), so implementation/reference existence is **not** proof that its current branch has live gyro deployment on that path.

**Unit distinction:** WinRT gyro emits deg/s. The custom legacy accelerometer exhibits g-scale physical behavior; both HHC and CTW interpret it as g. CTW also interprets the legacy gyro as deg/s from its field sweep. For CG3EM, this is a strong reference-based **initial units assumption**, not a verified formal Windows sensor API unit declaration. Do not apply radians/degrees or 9.80665 conversions without a demonstrated source contract.

### 3.4 Target-specific Steam Deck encoding (PR2 only)

HHC's target reference uses:
```text
Accel fields: (Accel.X, -Accel.Z, Accel.Y) * 16384 counts/g
Gyro fields:  (Gyro.X,  -Gyro.Z,  Gyro.Y) * 16 counts/(deg/s)
```

These are **target encoding rules, not physical sensor normalization**. PR2 must verify them against this Addon's actual canonical VIIPER Steam Deck native layout and ABI and apply them **once**; do not pre-encode in PR1. Quaternion/fusion is out of scope.

## 4. Gyro PR1 — Common production Motion Core

Implement in one focused PR:
1. A narrow immutable latest `MotionState` snapshot, independent of button/trigger `ControllerState`, containing normalized gyro (deg/s), accelerometer (g), independently tracked timestamps and validity/freshness, model/backend source evidence, and an unavailable/neutral representation.
2. Bounded, exact-known-model source discovery. A2VM prefers WinRT gyro and legacy physical accel; CG3EM prefers legacy physical gyro and physical accel. Do not select by shared custom type GUID alone; validate role, sensor ID, XYZ fields, identity/readiness and finite live values. A2VM 7 and 8 share an initial policy but retain exact model identification.
3. One small Runtime-owned motion service (no new controller authority). It owns its two sensor reader lifetimes and one atomic latest-snapshot handoff; use independent reader work so blocking accel/gyro calls cannot hold each other or the controller publisher.
4. Apply the HHC-current common gyro and accel sensor-to-application coordinate transform **once**, in the motion normalization boundary.
5. Preserve fresh/duplicate semantics, independent timestamps and bounded staleness; never replay stale nonzero rate or manufacture gravity. Require **both valid physical gyro and physical acceleration** for PR2's usable Steam Deck IMU capability.
6. Lifecycle: initialize only after admitted/owned Full1902 controller startup; remain independent of Steam/BPM virtual switching; invalidate on suspend, motion source loss and relevant PnP/physical recovery; reacquire fresh sensor objects after resume; dispose/stop before controller-owner teardown. Motion failures remain feature-local and leave ordinary controls working.
7. Prefer minimal, evidence-backed bias subtraction only if the existing stationary captures and clean deterministic implementation support it. No hard-coded calibration offsets, persistence, synthetic gravity, generic sensor fusion or configurable motion curves. Do not make uncertain EX bias calibration a PR1 blocker.
8. Unit tests for selection, axis/sign, units assumptions, coherent concurrent snapshots, independent freshness, failed reads/stalls, start/stop/restart and lifecycle invalidation using simulated sensor readers. Existing probe/Full1902 tests must remain passing.

**PR1 ends with no edits to the canonical SteamDeck mapper/publisher or VIIPER device state, no Steam Input or Xbox360 output, and no feature UI.** Build and unit tests are merge criteria; real-device acceptance is not.

Full implementation contract: [Gyro PR1 work order](../work-order/GYRO_PR1_FULL1902_COMMON_MOTION_CORE_WORK_ORDER_2026-10-09.md).

## 5. Gyro PR2 — Steam Deck only (planned, not yet ordered)

Wire PR1's latest motion snapshot as an optional, nonblocking input to the **existing** `CanonicalSteamDeckInputPublisher` / `SteamDeckDeviceStateMapper` / native `SteamDeckDeviceState`. Do not create an IMU publisher, change canonical cadence, add a virtual device, or affect the X360 publisher.

- Real normalized gyro and real normalized accelerometer must both be available and fresh for meaningful Steam Deck IMU output.
- Apply the VIIPER target sign/axis/scaling exactly once, saturate/clamp native signed fields rather than overflow, handle unavailable data as neutral/unavailable with appropriate semantics.
- Preserve the current canonical buttons, stick/touch, haptics/rumble, Steam/QAM, attach/detach and native output ABI.
- Steam Input continues to own gyro activation, sensitivity, mouse/stick mapping and per-game behavior. No Addon Steam-gyro settings UI.
- Verify target IMU report packing and Steam Input behavior by tests where possible. Device results for both A2VM and EX are user **post-merge** responsibilities.

The PR2 work order will be drafted separately after PR1 implementation/review.

## 6. Explicit later work: Xbox360

XInput Xbox360 has no gyro IMU slots. A future **separate** PR track can consume exactly the same common PR1 `MotionState` but must implement gyro-to-right-stick/activation/sensitivity separately, with clearly defined combination with the physical right stick and Xbox game profiles.

**No gyro-to-right-stick, gyro-to-mouse, Xbox mappings/UI/profiles or X360 state modifications in PR1 or PR2.**

## 7. Failure policy / non-overengineering

Real supported lifecycle hazards to cover:
- sleep / hibernate / resume;
- controlled restart / crash / shutdown;
- physical controller/session disappearance, PnP re-enumeration;
- stale or indefinitely blocked sensor reads;
- sensor unavailability, invalid data and source-reacquisition failures.

Policy:
- **Fail passive for motion**: no stale nonzero gyro, no invented gravity; never fail the ordinary Full1902 controller or reverse PID1902 based on a gyro failure.
- Respect existing Full1902 power, routing rollback, HidHide and VIIPER owner/teardown. Do not add new authorities, global state machines, additional VIIPER publishers, timekeeping frameworks, periodic device rescans or separate PnP ownership.
- No speculative interleaving locks/epochs/managers. A small synchronization boundary for coherent gyro+accel snapshots is justified by the two real independent sensor readers.
- Runtime may log source selection, units assumption, stale transition, acquisition failure and recovery with rate limits; no always-on raw CSV sampling or log flood.

## 8. Evidence and validation ledger

| Evidence / decision | State |
|---|---|
| A2VM 8 WinRT gyro + legacy accel | User 2026-10-09 probe demonstrated |
| A2VM physical axes (X pitch, Y roll, Z yaw) | User 2026-10-09 axis capture demonstrated |
| HHC/CTW gyro transform `(X,Z,-Y)` | Both references agree |
| A2VM/EX accelerometer transform `(X,Z,-Y)` | Current HHC config chosen over CTW Dev legacy mapping; output validation pending |
| EX legacy sensor readers | User 2026-09-05 probe demonstrated |
| EX gyro 98 Hz + sporadic 120–141ms call stalls | User 2026-09-05 probe demonstrated |
| EX axis/sign physically checked | Pending post-merge |
| A2VM 7 separately checked | Pending post-merge |
| Steam Deck native IMU values consumed by Steam Input | Pending PR2 + user post-merge verification |
| Xbox360 gyro behavior | Deferred, not part of PR1/PR2 |

## 9. Documentation precedence

For the PR1/PR2 motion implementation scope, this 2026-10-09 plan supersedes older SD6 research *implementation sequencing and the old pre-capture "wait for EX before coding" gate*. The old reports remain intact as provenance. **Full1902 controller/privilege/authority documents remain authoritative for ownership and lifecycle.** Current main code and the relevant active work order determine actual integration details.
