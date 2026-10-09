# Work Order — Gyro PR1: Full1902 Shared Production Motion Core (A2VM + CG3EM)

**Prepared:** 2026-10-09  
**Source baseline inspected:** main at a3d2e648726c37f52b3979e2b6771dd5af6f690b (followed by documentation-only plan commit 9a3ce835b0b61fd1ff1c78f62fbae19bd781ad79). Recheck actual main before coding.  
**Stage:** Gyro PR1 of PR1 common core → PR2 SteamDeck IMU → later separate Xbox360 gyro.  
**Ownership:** Local Codex implements and statically tests; user performs post-merge real-device validation.

## 0. Binding decisions and exit criteria

Implement one **production motion-acquisition and normalization module** covering **both** MSI Claw A2VM and EX/CG3EM. This is an independent component of the standalone Full1902 app, not an integration with CTW.

Supported model IDs and exact board IDs:
- **msi.claw.a2vm.7** — MS-1T42, Claw 7 AI+ A2VM;
- **msi.claw.a2vm.8** — MS-1T52, Claw 8 AI+ A2VM;
- **msi.claw.cg3em** — MS-1T91, Claw 8 EX AI+ CG3EM.

**PR1 stops at a normalized, independently fresh immutable MotionState snapshot.** Do **not** write any Steam Deck IMU fields or virtual Xbox360 stick fields in PR1. PR2 connects PR1 to SteamDeck only. Xbox360 is later, with separate activation/mapping work.

Implement EX in PR1 using the already physically proven legacy Sensor API source, with HHC-current axis mapping as the **initial reference**. Do not wait for new EX hardware testing before submitting or merging.

**Real-device validation is NOT a Codex task requirement, CI requirement or PR-review blocker.** The user tests after merge. Ordinary build and deterministic automated tests **are** part of Codex delivery.

## 1. Read these sources and respect precedence

**Current project authority:**
1. [Approved 2026-10-09 motion/Steam Deck two-PR plan](../gyro/FULL1902_GYRO_PR1_PR2_IMPLEMENTATION_PLAN_2026-10-09.md).
2. [Full1902 documents authority README](../Full%201902%20Implementation/README.md).
3. [Full1902 canonical controller architecture](../Full%201902%20Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md).
4. [Full1902 elevated Runtime architecture](../Full%201902%20Implementation/FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md).
5. [Prior SD6 research](../gyro/GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md); preserve its one-authority/nonblocking/fail-passive principles. Its old EX-research-before-coding gate is superseded by the accepted implementation plan.
6. [EX direct hardware results 2026-09-05](../gyro/CG3EM_LIVE_SANITY_HARDWARE_RESULTS_2026-09-05.md).
7. [Existing diagnostic sensor probe work order](../gyro/SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md) and [capture PR-B](../gyro/SD6A_CLAW_SENSOR_PROBE_PR_B_CAPTURE_MODES_AND_SUMMARIES_WORK_ORDER.md).

**New measured A2VM evidence:**
- [2026-10-09 A2VM ClawSensorProbe sessions](https://drive.google.com/drive/folders/1hzsKL82VmSPfJYls6zY4Kn2gmADTT6Ki).
- [Full axis-characterization session 20261009-112331](https://drive.google.com/drive/folders/1DP_-SKNgGiMtOvr4tiPQfWnZN6bwHrgD).
- A2VM 8/MS-1T52: physical gyro X=pitch, Y=roll, Z=yaw; WinRT gyro in deg/s at ~100Hz; legacy Physical Accelerometer in g at ~300Hz. The distinct A2VM 7 board shares the proposed initial code policy but was not measured by this dataset.

**External physical-motion references only (not dependencies):**
- [HHC A2VM config](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Resources/Devices/ClawA2VM.json).
- [HHC CG3EM config](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Resources/Devices/ClawCG3EM.json).
- [HHC Windows Sensor API](https://github.com/Valkirie/HandheldCompanion/blob/main/HandheldCompanion/Sensors/WindowsSensorManager.cs).
- [CTW Dev ClawGyroSourceAdapter](https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/XboxGamingBarHelper/ControllerEmulation/ClawGyroSourceAdapter.cs).
- [CTW Dev SensorApiGyroSourceAdapter](https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/XboxGamingBarHelper/ControllerEmulation/SensorApiGyroSourceAdapter.cs).

Current source code and Full1902 lifecycle/ownership take precedence over historical file descriptions. Do not transplant CTW/HHC implementation or copy it as a dependency; the research is about sensor behavior and conventions only.

## 2. Verified actual code / insertion seams

These existed on the inspected main:

| File | Code responsibility / limit |
|---|---|
| src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawDeviceModels.cs | Exact board/model resolver and three target IDs. |
| src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs | Existing Runtime owner. Startup in TryStartDisabledModeControllerAsync; recovery in RecoverOwnedControllerPhysicalInputAsync; QuiesceFull1902PresentationForSuspendAsync and OnPowerResumeObserved; BeginProcessShutdown / DisposeAsync. |
| src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs | Sole PID1902, HIDHide, DirectInput owner. Preserve. |
| src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs | Sole physical controller reader; LatestState; cannot be blocked by motion. |
| src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs | Sole virtual presentation owner; must not change in PR1. |
| src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs | Existing 4ms/250Hz native Deck publisher; leave unmodified in PR1. |
| src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs | Intentionally zero IMU currently; leave unmodified in PR1. |
| src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalViiperNativeTypes.cs | Native state already exposes gyro/accel slots; leave unmodified in PR1. |
| src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeContracts.cs | ClawSensorDiscovery.Select, SensorId dedup and role validation, timestamp dedup. |
| src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeSensorApi.cs | Already working Sensor API, direct type discovery, GetSensorById, PID7/8/9 reading, COM ownership. |
| src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeWinRtSources.cs | Working WinRT reader, Gyrometer/Accelerometer opening, timestamp, report interval restore. |
| src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeReaders.cs | Existing **diagnostic** two-reader threading and safe release reference, not a production data sink. |

**Do not** create another physical controller owner, another DirectInput instance, HidHide whitelist consumer, VIIPER runtime/typed device, virtual-output publisher, power authority, Windows service, or CTW IPC. The new motion module is a subordinate Runtime-local capability.

## 3. Required PR1 architecture

~~~
Exact Center M Disabled + admission ready + supported resolved board
   |
Existing Full1902 owned PID1902 / DirectInput (untouched)
   |
Host-owned Motion Source (one owner, independent of Steam/BPM)
   |                                       |
Gyro reader                             Accel reader
A2VM: WinRT                             A2VM: legacy COM
CG3EM: legacy COM                       CG3EM: legacy COM
   |                                       |
Own worker; never block DI or virtual publisher
   +----------------------+----------------+
                          |
                    Normalize once
                          |
         Coherent immutable latest MotionState
        (gyro/accel separate timestamps + validity)
                          |
                        PR1 END
                         /   \
         PR2 SteamDeck IMU   Future Xbox360 gyro-stick
~~~

Keep implementation focused: a MotionState data type, one production MotionSource with two low-level independent readers, and narrow glue in AddonProcessHost. Small local internal abstractions for deterministic tests are justified; broad generic sensor registries, message buses, multi-owner coordinators, synchronized sensor fusion pipelines or a new scheduler are not.

### 3.1 MotionState contract

An immutable last-snapshot representation separate from ControllerState, containing:
- GyroX/Y/Z in **deg/s**, AccelX/Y/Z in **g** after one source-to-application transform;
- separate gyro and accel **monotonic receive timestamps** and, when available, separate original sensor timestamps;
- separate HasGyro / HasAccelerometer validity, source/backend identity, and a trivial combined IsUsableForSteamDeckImu decision;
- a neutral/unavailable effective snapshot for consumers (PR2 must not guess if a previous nonzero reading is stale).

Suggested testable shape, not mandatory naming:

~~~csharp
internal sealed record MotionState(
    double GyroXDegPerSecond, double GyroYDegPerSecond, double GyroZDegPerSecond,
    double AccelXG, double AccelYG, double AccelZG,
    long GyroReceiveTicks, long AccelReceiveTicks,
    bool HasGyro, bool HasAccelerometer,
    string GyroSource, string AccelSource);
~~~

Sensor receive-time monotonic freshness must not use local wall-clock time (NTP/clock change). Since **two workers mutate one combined snapshot**, use one small, contained synchronization/merge boundary so accel publishing cannot overwrite a newer gyro sample and vice versa. A single immutable value exposed by atomic/Volatile reference read is appropriate; no sensor/COM locks on consumer reads. Holding a lock only while merging a tiny snapshot is practical, not theoretical race overengineering.

Avoid extra phase/epoch/cycle/queue objects. PR1 does not touch gamepad ControllerState or virtual state.

## 4. Exact device policy / source discovery

Selection is bounded by exact supported model identity from the existing resolver and AddonProcessHost startup result (HardwareDeviceModel); do not use product-name contains, broad MSI friendly-name matching, or a GUID-only hardware decision.

**A2VM 7 and A2VM 8:**
- Gyro: WinRT **Gyrometer**, reading fresh AngularVelocityX/Y/Z in degrees per second. WinRT report minimum ~10ms; expected approximately 100Hz.
- Accel: legacy Windows Sensor API **Physical Accelerometer**, custom SensorType E83AF229-8640-4D18-A213-E22675EBB2C3, custom FMTID B14C764F-07CF-41E8-9D82-EBE3D0776A6F, property IDs 7/8/9, measured g.
- No dependency on a WinRT Accelerometer; the 2026-10-09 A2VM capture found it unavailable.

**CG3EM / EX:**
- Gyro: legacy Windows Sensor API **Physical Gyrometer**, same custom type/FMTID/PID7/8/9.
- Accel: legacy Windows Sensor API **Physical Accelerometer**, same custom type/FMTID/PID7/8/9.
- Two **different** logical sensor IDs. The custom GUID also belongs to Simple DMD/Shake Gesture/Calibrated Accelerometer, so a type-only pick is wrong.
- EX captured ~98Hz gyro and 273–276Hz accel with sporadic gyro COM GetData blocking ~120–141ms, no source read failures.

Re-use the existing ClawSensorDiscovery.Select role-aware exact SensorId de-duplication (broad category + GetSensorsByType can represent the same logical device twice) or extract the minimum shared selection method. Verify ready/usable state, proper role/name, path or known Intel sensor-stack identity where available, required finite XYZ support and actual live samples. No arbitrary HID gyro fallback or unrelated sensor selection.

If a valid role cannot be selected, **motion is unavailable and ordinary Full1902 gamepad still works**. Startup should not be blocked by optional gyro.

## 5. Units and normalization: one place, both models

Source readings use the following **initial production interpretation**:
- A2VM WinRT Gyro: degrees/second, directly declared by the Windows API.
- A2VM and EX legacy physical accelerometer: g, supported by observed stationary magnitude ~1g.
- EX legacy physical gyro: degrees/second, supported by CTW Dev measured field sweep and HHC path. **Note** the prior ClawSensorProbe recorded legacy UnitBasis=Unknown, so this EX interpretation is external-reference-based and needs later user device validation.

Use exactly one transform at the motion normalization boundary for both A2VM (7 and 8) and EX:

~~~csharp
// Sensor-space -> common application-space, no native SteamDeck scaling:
gyro  = new Vector3(rawGyro.X,  rawGyro.Z,  -rawGyro.Y);  // deg/s
accel = new Vector3(rawAccel.X, rawAccel.Z, -rawAccel.Y); // g
~~~

HHC current A2VM/CG3EM and CTW Dev **agree on gyro** (X,Z,-Y). HHC current A2VM/CG3EM says **accel (X,Z,-Y)**. The CTW Dev adapter uses older A1M **accel (-X,-Z,Y)**. This difference is real; use current HHC config and record EX/target validation as pending. Do **not** mix conventions.

Historical 2026-09-05 SD6 §8 preliminary gyro orientation (X,Y,-Z) predates the current 2026-10-09 code/evidence comparison and is superseded for this PR.

**Do not convert** deg/s to rad/s, g to m/s², gyro to native Deck *16 counts/deg/s, or accel to native Deck *16384 counts/g in PR1. Those last two factors and the target-specific field orientation belong exclusively to PR2.

**Zero-rate bias:** Stationary captures show small nonzero resting offsets, but they do not justify hard-coding identical numeric offsets for every A2VM/EX unit. Do not build a new calibration framework in PR1. Retain the raw normalized deg/s (unbiased); PR2/post-merge output evidence can justify a small, explicitly scoped, stationary-bias subtraction follow-up if Steam drift is demonstrated. Do not add static deadzone, smoothing, filters, fabricated gravity, fusion, or quaternion.

## 6. Independent reads and freshness

1. Never call WinRT GetCurrentReading or COM GetData from the 250Hz controller DirectInput loop, VIIPER native publisher, Steam/BPM presentation reconcile, WinUI dispatcher or any Full1902 ownership lock.
2. Keep separate reader lifetime per physical sensor role. Open/read/dispose COM objects from the owning reader context/thread; do not release a COM pointer from a different worker during an outstanding blocking read.
3. For WinRT samples, preserve original reading timestamps; dedup repeated GetCurrentReading timestamps. For legacy, use the existing report timestamp if supported; also capture monotonic receiving time.
4. Reject nonfinite samples and invalid/ambiguous role data. Bounded logging only for source selection, missing source, failure, stale transition and recovery; no continuous production CSV.
5. Gyro and accel refresh independently: ~100Hz vs ~275–300Hz; do not synthesize 250Hz physical motion samples.
6. Put **named internal freshness constants** in one place, conservative **initial policy**, not claims of observed device boundaries: gyro at most **250ms** old; acceleration at most **500ms** old. Revisit with user post-merge data, not speculative complexity.
7. Each consumer read must be quick/nonblocking and expose effective validity:
   - neither role has data → unavailable;
   - only one role fresh → combined SteamDeck IMU unavailable;
   - gyro stale → no stale nonzero angular velocity can be exposed as valid;
   - accel stale → no synthetic gravity, combined unavailable;
   - fresh gyro zero (real stationary sample) remains valid zero;
   - brief legacy GetData stall stays isolated from controller state publication; after stale threshold expires, mark motion unavailable.
8. Prevent stale-source values from resurrecting after start/stop/restart. State publication remains one atomic coherent immutable snapshot.

No continuous polling from a UI/control read path.

## 7. Full1902 lifecycle integration — exact host points

### 7.1 Startup

In **AddonProcessHost.TryStartDisabledModeControllerAsync**:
- Require exact Center M Disabled authority and successful DisabledBootAdmission; ordinary Full1902 startup already checks these.
- Only construct/start MotionSource after **owner.AcquireAsync** reports Owned and **LiveInputSource.IsRunning** is true. Provide resolved exact HardwareDeviceModel; unsupported models have no motion.
- Motion start failure logs a rate-limited warning, publishes unavailable snapshot and **does not** fail VIIPER readiness, controller startup, physical mode, routing or WING suppression.
- Motion lives across X360 ↔ SteamDeck attachment changes without being stopped/restarted; this is a **source** for either future output presentation.
- Add only a small owned field in AddonProcessHost (not a new process- or route-wide authority). No new startup task.

### 7.2 Suspend / Hibernate

Follow **the existing Full1902SuspendParticipant / QuiesceFull1902PresentationForSuspendAsync**:
- Invalidate latest motion **before** returning from suspend quiesce so pre-suspend gyro cannot be mistaken for valid on resume.
- Stop/quiesce readers and ensure old sensor handles are not reused. If COM GetData blocks, cancellation may be observed only after it returns; release the COM object on its owning worker, using bounded teardown/deferred cleanup rather than forcing cross-thread release.
- Preserve existing physical DirectInput, presentation neutral, rumble STOP, VIIPER, power gate and HidHide invariants.
- Do not create a second power participant, extra resume state machine, retry watchdog or global coordinator.

### 7.3 Resume

Use **AddonProcessHost.OnPowerResumeObserved** as the existing power boundary, but respect that existing Full1902 presentation recovery may still be reconciling:
- Set motion unavailable until re-acquired.
- Acquire **fresh** WinRT/COM handles only when current Full1902 physical ownership is proven healthy; otherwise allow existing recovery to finish.
- Clear timestamps and startup reader state, then accept only new fresh samples.
- A sensor failure remains feature-local. The user can still operate the ordinary physical/virtual gamepad.

### 7.4 Device loss / PnP re-enumeration

Use **OnOwnedControllerPhysicalInputCompleted** and **RecoverOwnedControllerPhysicalInputAsync** as existing notification/repair facts:
- Immediately invalidate motion after real owned DirectInput loss.
- On successful real same-device physical recovery, re-acquire motion sources; do not perform a mode switch or PID check from the motion reader itself.
- If sensors independently disappear without DirectInput loss, failed reads invalidate their role. A small bounded source-local retry/reopen after a meaningful real error is okay; never rescan all devices in a tight loop or construct a second PnP/physical-owner watcher.
- Motion failure never triggers PID1901 restoration, HidHide mutation, VIIPER detach or physical input recovery.

### 7.5 Controlled release and shutdown

Follow **BeginProcessShutdown** and **DisposeAsync** ordering:
- Block new motion startups/reacquisition and immediately publish unavailable/neutral state.
- Before disposing physical ownership, drain/stop motion readers and dispose worker-owned COM and WinRT handles. Existing presentation-before-physical teardown stays unchanged.
- The **Enable Center M and Restart** stock-authority release path must also stop motion; do not leave live sensor work after physical controller authority release.
- Idempotent repeated stop/dispose; bounded native read shutdown with safe deferred owned release if a delayed call is still in flight.
- Do not change the existing Full1902 PID1901 ↔ PID1902 release or fail-close rules.

### 7.6 Diagnostic coexistence

The Developer ClawSensorProbe retains separate session ownership/CSV/statistics. Do not make production motion import its session writer, RPC, capture phases or reports. If the developer diagnostic runs at the same time as production, each reader retains its own handles; neither can release the other's pointers. Do not add a generalized sensor arbitration framework for this alone.

## 8. Narrow implementation file plan

Recommended smallest coherent footprint; adjust class names to match current code:
- **New:** src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawMotionState.cs — immutable snapshot, freshness, application-space transform.
- **New:** src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawMotionSource.cs — one owner, exact model/backend selection, two independent readers, current-state merge, lifecycle.
- **Potential minimal reuse/extraction only if warranted:** src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeContracts.cs, ClawSensorProbeSensorApi.cs, ClawSensorProbeWinRtSources.cs. Preserve diagnostics.
- **Small integration:** src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs — create after ownership proof; invalidate/reacquire on existing power/recovery seams; stop before physical release.
- **Tests:** tests/SteamInputAddonforClaw.Tests/MsiClawMotionSourceTests.cs (and optional focused normalization test file).

Explicitly leave the canonical SteamDeck/Xbox360 mapper, native types, VIIPER publisher, presentation selection policy, SteamInput settings, profiles and frontend UI untouched in PR1.

## 9. Required deterministic automated verification

Use fake source/read results and a fake monotonic clock, not Windows sensors or wall-clock-sensitive sleep asserts.

**Model and sensor identity**
- A2VM7 / A2VM8 choose WinRT gyro + unique correct Physical Accelerometer.
- CG3EM chooses **two separate** correct legacy Physical gyro and accel SensorIds even with shared custom GUID.
- Wrong friendly name (Simple DMD, Shake Gesture, Calibrated Accelerometer), missing XYZ, ambiguous candidates and duplicate enumerations are rejected or deduped appropriately.
- Missing sensor/invalid model returns unavailable; no ordinary controller failure.

**Axis / units**
- Gyro input (1,2,3) produces (1,3,-2) deg/s once.
- Accel input (0.1,0.2,0.9) produces (0.1,0.9,-0.2) g once.
- No radians/second, acceleration m/s², Steam Deck native count scaling, or old CTW A1M accel sign is introduced.

**Freshness / concurrency**
- Concurrent gyro and accel publish yields coherent latest state without losing either role.
- Identical WinRT timestamps deduplicated.
- Nonfinite values rejected; independent stale data invalidates combined motion without stale nonzero gyro or fabricated gravity.
- Fresh true zero gyro is accepted.
- Slow/blocking legacy read never blocks caller reading latest MotionState or DirectInput/latest-controller publication.

**Lifecycle**
- Disabled-boot denied/Center M Enabled/model unsupported does not start any sensor.
- Valid ownership starts one source regardless of active X360 or SteamDeck presentation; no new device or native output.
- Start failure does not fail physical controller/presentation.
- Suspend resets freshness; resume reopens handles, only new samples become valid.
- Physical input loss invalidates; successful physical owner recovery allows motion re-acquisition; failed recovery stays unavailable.
- Controlled stock enable/shutdown stops/disposes readers; no unsafe cross-worker pointer release; repeated stop is safe.

Run the repository's full .NET test suite, including existing diagnostic, physical-owner, publisher, host startup/resume/shutdown tests:

~~~powershell
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj
~~~

Compile relevant app projects under normal CI configuration. Tests are static/simulated. The developer does not need an A2VM or EX device for PR acceptance.

## 10. Prohibited work and overengineering guardrail

NO:
- Steam Deck IMU writes, target pitch/yaw/roll conversion, quaternion, state mapper changes, publication timing changes or VIIPER-native changes (**PR2**).
- Xbox360 gyro-to-stick/mouse, sensitivity, activation rules, profiles, UI, or Xbox publisher changes (**later**).
- Gyro-to-mouse firmware/vendor-HID fallback.
- Synthetic gravity, orientation fusion, persistent calibration database, fixed hardware zero-rate offsets, arbitrary deadzones or filters.
- Additional physical controller owner, second DI reader, HidHide authority, route state machine, sensor service process, power participant or PnP watcher.
- Hypothetical race-only locks/epochs/barriers/managers/abstractions.
- CTW integration, CTW IPC or dependency.

Supported scope is **one Windows user and one interactive session**. Fast User Switching, RDP and multi-session are unsupported.

## 11. Handoff / PR acceptance

PR1 is complete when:
- [ ] Three exact MSI Claw models implemented, EX not postponed.
- [ ] WinRT/legacy role-specific acquisition and independent nonblocking readers implemented.
- [ ] Units / one common normalized axis transform implemented, coherent immutable snapshot and bounded freshness tested.
- [ ] Runtime ownership, suspend/resume, controlled release, device-loss/recovery and shutdown integrated without altering existing Full1902 controller authority.
- [ ] Existing diagnostic and controller tests pass; new deterministic tests demonstrate real source failure/staleness behavior.
- [ ] No SteamDeck or Xbox360 motion presentation and no UI introduced.
- [ ] PR description accurately flags **post-merge user checks**: EX axes and legacy gyro units, A2VM7 hardware behavior, A2VM/EX target orientation and drift.

**DO NOT require real hardware validation as a code-review, CI, or merge blocker.** A materially correct implementation can be merged, then user checks actual hardware. PR2 will follow as a separate detailed order once the common core lands.
