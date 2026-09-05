# Steam Addon for Claw — Gyro / IMU Research Consolidation and SD6 Design

Date: 2026-09-05  
Repository: `onehoon/SteamAddonforClaw`  
Current controller architecture: Full1902, canonical VIIPER Steam Deck presentation (`28DE:1205`)  
Feature track: SD6 — gyro / accelerometer  
Status: **research/design only; no production gyro claim**

---

## 1. Executive conclusion

The gyro path is now materially clearer than it was during the August CG3EM investigation.

The correct product direction remains:

```text
physical MSI Claw motion sensors
        ↓
model/capability-specific Windows acquisition
        ↓
normalized MotionState
        ↓
device-axis transform + measured zero-rate correction where justified
        ↓
Steam Deck IMU mapping
        ↓
existing CanonicalSteamDeckInputPublisher
        ↓
canonical VIIPER Steam Deck 28DE:1205
        ↓
Steam Input
```

The Addon should **not** implement gyro-to-mouse, gyro-to-stick, sensitivity curves, activation rules, smoothing profiles, or game-specific gyro behavior. Those are Steam Input responsibilities.

The important change from the older research is that gyro and accelerometer must be treated as one usable Steam Deck IMU capability. New WSGM A2VM hardware evidence shows that a virtual Deck with meaningful gyro but an all-zero accelerometer can be interpreted by Steam as a freefall-like state and lose usable gyro processing. The initial SD6 target therefore should be **real gyro + real accelerometer**, not gyro-only output with neutral acceleration.

For CG3EM specifically, production implementation must still wait for a fresh repaired-driver capture. We now have a much better diagnostic recipe for that capture, but we do **not** yet have evidence that CG3EM exposes exactly the same sensor type GUID, device path, field layout, axis transform, or zero-rate bias as A2VM.

---

## 2. What is current Addon architecture vs historical material

### 2.1 Historical research used Classic Steam Controller assumptions

The early `gyro.txt` and 2026-08-12 CG3EM re-analysis were written while the project was still reasoning around a Classic Steam Controller style output path.

That material remains valuable for:

- raw IMU acquisition research;
- Windows Sensor API observations;
- HHC / ClawTweaks provenance;
- why firmware gyro-to-mouse is not an acceptable raw source;
- early axis and scaling research.

It is **not** the current virtual-output architecture.

### 2.2 Current Full1902 target is Steam Deck

Current main owns a canonical Steam Deck path through VIIPER. The production mapper deliberately leaves the IMU and quaternion fields neutral until SD6 is implemented and hardware-validated.

The existing `CanonicalSteamDeckInputPublisher` already owns the production Steam Deck publication cadence. It runs on the Addon's real-hardware-tuned absolute-deadline high-resolution timer path at approximately a 4 ms logical period. SD6 should compose motion into that state; it should not create a second virtual-device owner or a second independent Steam Deck publisher.

The Full1902 authority rules remain unchanged by gyro:

- physical controller authority is still one owner;
- PID1902 / DirectInput / HidHide / VIIPER lifecycle remains owned by the existing runtime;
- Steam/BPM still chooses X360 vs Steam Deck presentation;
- motion is a capability of the Steam Deck presentation, not a new authority layer;
- suspend/resume and physical-device loss must reacquire sensor handles rather than assuming they survive lifecycle transitions.

---

## 3. CG3EM research timeline

### 3.1 Original research

The original work established the right architectural boundary:

```text
Claw raw IMU
→ virtual Steam controller IMU fields
→ Steam Input owns mapping/calibration behavior
```

It also correctly rejected using already-mapped mouse/right-stick output as if it were raw motion.

### 3.2 2026-08-12 CG3EM re-analysis

On the tested CG3EM installation at that time:

- Intel Integrated Sensor Solution / ISH was present;
- Center M firmware gyro-to-mouse still worked;
- present Windows Sensor inventory was absent;
- `ISensorManager.GetSensorsByCategory(SENSOR_CATEGORY_ALL)` failed with `0x80070490`;
- the Addon's ClawSensorProbe and an independent no-SDK probe failed at the same discovery boundary.

That established an important fact about the **installation state**: the Addon could not obtain a raw Windows sensor on that system at that time.

It did not prove that CG3EM hardware lacked a host-readable IMU.

### 3.3 2026-08-15 correction

After the relevant sensor / ISH driver stack was reinstalled, Windows sensor access recovered on the CG3EM machine.

Therefore the active interpretation is:

> The `0x80070490` session was a broken-driver-state observation, not a permanent CG3EM hardware limitation.

The 2026-08-15 correction explicitly requires a new repaired-driver capture before SD6 freezes:

- sensor-class PnP inventory;
- Intel ISH / child topology;
- WinRT and legacy Sensor API discovery;
- selected gyro and accelerometer identity;
- raw units;
- axis orientation;
- practical update rate/timestamps;
- app restart behavior;
- sleep/hibernate/resume behavior;
- disappearance/stall behavior.

That requirement still stands.

---

## 4. New external evidence from WSGM 2.0 / A2VM

This section is **external A2VM evidence**. It is directly useful to decide what the Addon should measure, but it is not permission to hard-code the same values for CG3EM.

### 4.1 Real accelerometer is available through legacy `sensorsapi`

WSGM's A2VM investigation found an STMicroelectronics LSM6DSO `Physical Accelerometer` exposed by Intel's sensor stack as a legacy custom sensor rather than a normal WinRT `Accelerometer`.

Device-verified A2VM reference values:

```text
Friendly name:
  Physical Accelerometer

Sensor type:
  E83AF229-8640-4D18-A213-E22675EBB2C3

Custom data format:
  B14C764F-07CF-41E8-9D82-EBE3D0776A6F

Acceleration fields:
  PID 7 / 8 / 9

Observed value type:
  VT_R4

Observed unit:
  g

Relevant Intel path identity:
  VID_8087&PID_0AC2
```

The important discovery mechanism is not only `GetSensorsByCategory(ALL)`. WSGM opens the known custom sensor class with:

```text
ISensorManager.GetSensorsByType(customSensorTypeGuid)
```

and then validates the candidate's friendly name, sensor type, ready state, device path, and supported XYZ fields before accepting it.

This is directly relevant because the current Addon diagnostic already declares `ISensorManager.GetSensorsByType` in its COM interface but its discovery path does not use it.

### 4.2 Gyroscope path is separate

On A2VM, WSGM uses WinRT `Gyrometer` for physical angular velocity and the legacy Sensor API for the custom accelerometer.

The A2VM gyro is reported in degrees/second and has a minimum report interval of approximately 10 ms, i.e. a practical ceiling near 100 Hz.

The production lesson is that the Addon should not require gyro and accelerometer to come from the same Windows API projection. A model may legitimately use:

```text
WinRT Gyrometer
+
legacy COM custom Accelerometer
```

while still publishing one normalized `MotionState`.

### 4.3 A zero accelerometer is not a safe Steam Deck placeholder

WSGM experimentally used synthetic gravity after observing that an all-zero accelerometer caused Steam's Deck gyro path to behave as if the controller were in a freefall-like state. After the physical accelerometer was found, that synthetic path was removed.

The Addon should take the same final lesson:

- do not ship synthetic gravity when a real accelerometer is available;
- do not pretend `Accel=(0,0,0)` is equivalent to a stationary controller;
- if the required physical acceleration source is not valid, keep SD6 motion passive/neutral rather than publishing a misleading IMU stream.

### 4.4 Accelerometer reads can stall independently

WSGM captured a real case where an accelerometer read blocked for roughly 200 ms and made an already-acquired gyro sample stale before publication. Their combined production path was changed so the potentially blocking acceleration read happens before the gyro acquisition, keeping the angular-velocity sample as fresh as possible.

The Addon's diagnostic currently polls the two legacy sensors on separate worker tasks, so that exact combined-read failure is not automatically reproduced. However, the diagnostic should record:

- individual `GetData` duration;
- sensor timestamp;
- receive timestamp;
- derived receive age;
- duplicate/no-data counts;
- long stalls.

Without those fields, a CG3EM trace may show a low apparent rate without revealing whether the sensor itself stalled, the COM call blocked, or the sample was simply duplicated.

### 4.5 Physical gyro zero-rate offset may need device-side subtraction

WSGM A2VM stationary captures measured a repeatable sensor-space zero-rate offset approximately:

```text
(+0.75, -0.37, -0.14) deg/s
```

That offset reached the virtual Deck report and caused visible drift. WSGM concluded that Steam did not reliably remove this target-side physical offset and added a rest-window offset estimator that performs subtraction only.

For the Addon, the lesson is **not** to copy those numbers. It is:

1. explicitly measure CG3EM stationary mean/span/noise;
2. determine whether the offset repeats across separate captures and orientations;
3. if a persistent hardware offset exists, subtract the measured offset before virtual Deck mapping;
4. avoid deadband/zero-hold as a substitute for proper offset correction.

A future production calibrator should remain model/data-driven and as simple as the hardware evidence permits. Do not add a generic fusion/calibration framework before the CG3EM capture demonstrates the need.

### 4.6 Re-acquisition is a real lifecycle concern, but should stay bounded

WSGM later hardened its bias acquisition so a false offset learned while the machine is moving can eventually be replaced by multiple later agreeing rest windows. That is useful evidence, but the Addon should not preemptively copy the full algorithm.

First determine what CG3EM actually does. A simple stable-rest mean subtraction may be enough. Only add re-acquisition logic if real device use demonstrates that startup-in-motion or resume creates a meaningful wrong-offset condition.

---

## 5. What the Addon can already reuse

### 5.1 Existing Steam Deck native ABI already has IMU fields

Current `SteamDeckDeviceState` includes acceleration, angular velocity and quaternion fields. Current `SteamDeckDeviceStateMapper` deliberately writes/keeps them neutral.

Therefore SD6 is not blocked on a new VIIPER device type.

### 5.2 Existing publisher should remain the only publication owner

Do not create:

- `GyroPublisher`;
- `MotionPublisher`;
- a second 100/250 Hz Steam Deck loop;
- another VIIPER lifecycle owner;
- another target-state authority.

The intended composition is conceptually:

```text
Latest ControllerState
Latest MotionState
        ↓
SteamDeck state composition
        ↓
CanonicalSteamDeckInputPublisher
```

The physical sensor cadence and virtual Deck publication cadence do not need to be identical.

### 5.3 Motion should stay separate from `ControllerState`

The controller's buttons/sticks/triggers and chassis IMU are different physical sources and have different lifecycle/freshness semantics.

Keep a narrow motion snapshot, conceptually:

```text
MotionState
  GyroX/Y/Z
  AccelX/Y/Z
  GyroTimestamp
  AccelTimestamp
  HasGyro
  HasAccelerometer
  backend / source identity for diagnostics
```

Do not turn `ControllerState` into a catch-all sensor model.

---

## 6. Recommended SD6 acquisition policy

### 6.1 Capability-based, model-bounded discovery

For each supported MSI Claw model, discover the sources actually available on that hardware/driver state.

The production source must be validated from multiple facts, not just one friendly name:

- expected model/family context;
- sensor API backend;
- sensor type;
- friendly name where stable;
- device path / Intel sensor-stack identity where available;
- sensor ready state;
- required XYZ data fields;
- finite live values;
- expected physical magnitude/unit behavior.

Do not infer that all `msi.claw` models share the same type GUID or axis transform.

### 6.2 Backend preference should reflect evidence, not ideology

A reasonable current target is:

```text
Gyro:
  WinRT Gyrometer when the repaired CG3EM capture proves the correct physical device
  legacy Sensor API only if CG3EM evidence requires it

Accelerometer:
  WinRT Accelerometer if a correct physical source exists
  otherwise legacy Sensor API custom sensor when validated
```

This is a design target, not a frozen fallback table. The CG3EM probe must establish the actual path.

### 6.3 No firmware gyro-to-mouse fallback

Controller-firmware gyro mappings are already transformed mouse/stick behavior. They are not raw angular velocity and must not be re-encoded into Steam Deck IMU fields.

If no valid raw host-readable motion source is available:

```text
controller routing remains functional
Steam Deck ordinary input remains functional
SD6 motion = unavailable / neutral
```

---

## 7. Freshness and stale-data policy

The exact thresholds must come from CG3EM capture, but the policy shape should be simple.

### Gyro

- publish only a recent physical angular-velocity basis;
- when the gyro becomes quiet or event-suppressed, represent physical rest as zero angular velocity rather than indefinitely repeating a nonzero stale value;
- do not keep a last nonzero rate forever;
- detect real long stalls separately from ordinary report jitter.

### Accelerometer

- prefer the latest real measured acceleration;
- a brief transient read failure may hold the last real acceleration for a bounded window if hardware evidence supports it;
- a long unavailable state makes the combined IMU capability unavailable rather than inventing gravity.

### Timestamps

The diagnostic and production path should preserve enough timing evidence to distinguish:

```text
sensor sample time
→ API read/receive time
→ normalized MotionState time
→ virtual Deck publication time
```

Do not add elaborate synchronization between independent sources unless real device testing shows that Steam Input needs tighter fusion than latest-valid snapshots provide.

---

## 8. Axis mapping policy

A2VM external evidence currently reports application-space mappings around:

```text
Gyro:  (X, Y, -Z)
Accel: (X, Z, -Y)
```

These are **A2VM references only**.

CG3EM must be characterized physically with an explicit phase capture:

- rest/flat;
- roll left/right;
- pitch up/down;
- yaw left/right.

The transform is frozen only after the signs and axis assignments are consistent across repeated captures.

The virtual Steam Deck encoder must apply the target transform exactly once. Avoid a stack where acquisition, normalization, mapper, and native encoder each perform their own undocumented swap/sign inversion.

One clear location should own the model's physical-to-application transform.

---

## 9. Zero-rate offset policy

Before implementing any correction on CG3EM, capture stationary data in at least two separate sessions and preferably at different physical tilts.

Report per axis:

- mean;
- standard deviation / noise measure;
- min/max/span;
- sample count;
- duration;
- effective rate;
- whether the offset remains similar across sessions and tilt.

Interpretation:

```text
repeatable mean substantially larger than stationary noise
+ invariant across tilt
→ likely zero-rate hardware/driver offset
```

If confirmed, use subtraction only:

```text
corrected = raw - measuredOffset
```

Do not start with:

- deadzone;
- zero-hold window;
- arbitrary hard-coded A2VM bias;
- orientation fusion;
- persistent per-device calibration database.

Add only what CG3EM evidence requires.

---

## 10. Quaternion / sensor fusion

Initial SD6 should leave quaternion neutral.

Do not synthesize orientation merely because the Steam Deck ABI exposes quaternion fields.

First milestone:

```text
real gyro
+
real acceleration
+
verified axis transform
+
verified freshness
```

Steam Input can consume the raw motion fields. A future quaternion/fusion track would require separate evidence that it materially improves a supported user scenario.

---

## 11. Full1902 lifecycle integration

Motion must follow the existing controller runtime lifecycle rather than inventing a new system-wide authority.

### Startup / controlled runtime restart

- acquire fresh sensor objects from current Windows reality;
- never persist COM/WinRT handles across process restart;
- if motion acquisition fails, keep ordinary controller presentation safe and mark SD6 unavailable.

### Sleep / hibernate / resume

Treat old sensor handles as disposable across power transition.

On resume:

```text
Full1902 controller reconcile completes / current device is known
→ reacquire motion sensors
→ revalidate identity and live samples
→ reset transient freshness/calibration state as appropriate
→ publish motion only after valid fresh data exists
```

Do not use pre-suspend nonzero gyro values after resume.

### Physical device loss / PnP re-enumeration

A missing or re-enumerated sensor source is a real lifecycle event. Motion should fail passive/neutral while the existing controller owner handles the main PID1902/PnP state.

Do not create an independent motion authority that fights the controller reconcile path.

### Shutdown

Stop sensor readers and release COM/WinRT objects before process-owned runtime teardown. No persistent sensor mutation should be left behind.

---

## 12. Developer diagnostics required before production SD6

Two current Developer Menu features should be upgraded before production gyro work.

### Environment Discovery Report

Needs a passive sensor/ISH section so a single environment report can answer:

- are Sensor-class and Intel ISH devices present?;
- what WinRT motion projections exist?;
- what legacy Sensor API candidates exist?;
- does direct custom-type lookup find a candidate even if broad enumeration is incomplete?;
- what HRESULT/state/device-path evidence explains failure?

See `DEVELOPER_DISCOVERY_REPORT_GYRO_REVIEW_2026-09-05.md`.

### Claw Sensor Probe / Gyro Test

Needs to move from the old "two friendly names in one legacy collection" assumption to backend-aware characterization, plus timing/stall and explicit stationary-bias evidence.

See `DEVELOPER_CLAW_SENSOR_PROBE_GYRO_REVIEW_2026-09-05.md`.

---

## 13. Recommended SD6 sequencing

### SD6-A — CG3EM repaired-driver characterization

No production routing change.

Deliverables:

- improved Environment Discovery sensor section;
- improved Claw Sensor Probe;
- CG3EM discovery report;
- live gyro/accel capture;
- axis-direction evidence;
- stationary offset/noise evidence;
- restart and resume evidence.

### SD6-B — normalized motion source

Implement only the physical acquisition and normalized `MotionState` path for the proven CG3EM source(s).

No Steam Deck IMU output yet.

### SD6-C — Steam Deck IMU mapping

Compose current `MotionState` into existing `SteamDeckDeviceState` and existing `CanonicalSteamDeckInputPublisher`.

Keep quaternion neutral.

Validate Steam Input sees stable motion and no freefall/neutral-accel failure.

### SD6-D — lifecycle validation and hardening

Hardware validate:

- startup;
- controlled runtime restart;
- sleep/hibernate/resume;
- sensor stall/disappearance;
- PID1902 physical-device loss/re-enumeration;
- SteamDeck ↔ X360 presentation transitions;
- teardown.

Only harden failure cases actually observed or realistically reachable in the supported single-user/single-session handheld lifecycle.

---

## 14. Explicit non-goals

SD6 does not require:

- gyro-to-mouse/stick algorithms;
- per-game gyro sensitivity;
- custom Steam Input replacement;
- sensor fusion framework;
- orientation/quaternion synthesis;
- generic Windows sensor manager abstraction;
- multi-session support;
- another controller authority;
- another VIIPER owner;
- another high-frequency virtual-output publisher;
- synthetic gravity as the default design;
- defensive state/lock/epoch machinery for purely theoretical interleavings.

The objective is one clear physical motion source, one normalized snapshot authority, and the existing Steam Deck publication path.

---

## 15. Evidence status matrix

| Item | CG3EM Addon evidence | A2VM WSGM reference | Production decision |
|---|---|---|---|
| Windows raw motion can exist | Sensor access recovered after driver repair; exact source must be recaptured | Yes | Re-probe CG3EM |
| WinRT gyro | Not yet freshly characterized after repair | Verified, ~100 Hz | Candidate, not frozen |
| Legacy custom accelerometer | Not yet freshly characterized after repair | Verified LSM6DSO custom sensor | High-priority CG3EM probe |
| A2VM custom type GUID | Not confirmed | `E83AF229-...` | Diagnostic reference only |
| Custom data PIDs 7/8/9 | Historical Addon/HHC evidence; needs fresh confirmation | Verified | Probe, then freeze if confirmed |
| Axis transform | Not frozen after repair | A2VM mapping known | Must capture CG3EM |
| Steam gyro needs plausible accel | Not yet independently verified on Addon CG3EM | Device-verified by WSGM | Design for combined IMU; validate locally |
| Gyro zero-rate offset | Not freshly measured | Repeatable A2VM offset measured | Add explicit CG3EM bias capture |
| Quaternion required | No evidence | WSGM final design avoids synthesis | Keep neutral |
| Existing 250 Hz Deck publisher reusable | Yes, current Addon architecture | N/A | Reuse; no second publisher |

---

## 16. Primary sources / provenance

### Addon repository

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/VIIPER_MIGRATION_TODO.md` — SD6 track
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs`
- `src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckInputPublisher.cs`
- `src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/*`
- historical files collected in this `docs/gyro/` folder

### WSGM / A2VM external reference

Relevant 2026-09-03 commits include:

- `KillerPixelCrew/WSGM.Device.Msi.Claw8A2Vm@80243d6` — physical accelerometer through `sensorsapi`;
- `...@c528c32` — read physical gyro after potentially blocking accelerometer acquisition;
- `...@f385191` — measured physical gyro zero-rate offset and subtraction;
- `...@ad393d2` — bounded offset re-acquisition and motion-path trimming;
- corresponding WSGM host commits documenting the Steam Deck behavior and device observations.

These commits are research/reference material. They do not establish CG3EM identity or calibration values.

---

## 17. Final design statement

The project no longer has a conceptual gyro blocker. It has a **CG3EM characterization task**.

The next correct move is not to build a large motion framework. It is to improve the two existing developer diagnostics, capture the repaired CG3EM sensor stack correctly, and then implement the smallest model-bounded `MotionState` source that the evidence supports.

Once that source is proven, the current Full1902 Steam Deck publication architecture already provides the correct place to emit the IMU fields.
