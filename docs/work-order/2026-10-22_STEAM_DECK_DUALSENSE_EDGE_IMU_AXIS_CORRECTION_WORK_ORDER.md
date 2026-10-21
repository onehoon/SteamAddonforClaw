# Work Order — Correct Steam Deck and DualSense Edge IMU Output Axes (Full1902)

**Date:** 2026-10-22  
**Repository:** onehoon/SteamAddonforClaw  
**Destination:** docs/work-order  
**Implementation:** One small, focused PR by local Codex  
**Hardware validation:** User-owned **after merge**, never a local-Codex acceptance criterion or PR review blocker  
**Status:** Implementation requested; this work order does not implement the fix

## 1. Goal and decision

Correct the existing Steam-facing virtual-controller IMU output mapping for **both Steam Deck and DualSense Edge**.

The physical sensor readers, shared MSI Claw motion normalization, virtual-controller ownership, and source freshness logic already exist and must remain unchanged. The defect is at the controller-specific output boundary: the Steam Deck and Edge mappers currently use incorrect axis/sign permutations when converting the already-normalized motion snapshot into their distinct native HID report fields.

Implement the two proven consumer-facing transforms below. Update existing deterministic unit tests so they assert the corrected output, rather than treating old behavior as a compatibility contract. Also correct the two misleading Yaw instructions in the read-only Claw Sensor Probe UI to make the user's subsequent physical characterization meaningful.

This is a **target-output mapping correction**, not a sensor source redesign, a new feature, a motion runtime refactor, or CTW integration.

## 2. Required design/architecture authority

Read these current repository documents before coding:

1. docs/Full 1902 Implementation/README.md — document precedence.
2. docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md — single physical owner, single active VIIPER presentation.
3. docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md — ownership/recovery and safe teardown.
4. docs/gyro/FULL1902_GYRO_PR1_PR2_IMPLEMENTATION_PLAN_2026-10-09.md — shared motion source and normalization.
5. docs/DUALSENSE_EDGE_PRESENTATION_ARCHITECTURE_2026-10-11.md — Edge target-specific native IMU fields and units.

Historical design documents describing CTW integration are NOT part of the current product contract. The Addon is a **standalone Full1902 application**. CTW/HHC and SDL sources are evidence/reference only; no CTW IPC, runtime, process, dependency, source adapter or coupling is permitted.

Supported product assumption: one Windows user and one interactive session; no multi-session/RDP/Fast User Switching work. Preserve actual suspend/hibernate/resume, restart/crash/shutdown, PnP loss/re-enumeration, PID1901/PID1902, HidHide, VIIPER native teardown and fail-close safety, without adding new lifecycle machinery.

## 3. Source-verified baseline and evidence

### 3.1 Actual Addon source files

- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawMotionState.cs
  - Shared TryNormalizeSensorAxes currently returns normalized = (rawX, rawZ, -rawY) for **both** gyro and accelerometer.
  - Leave this method, the immutable snapshot shape, source validity and independent freshness rules unchanged.
- src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawMotionSource.cs
  - Existing reader publishes normalized snapshots through the shared core. Do not edit its read/COM/WinRT/cadence/selection policy.
- src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckDeviceStateMapper.cs
  - Currently maps Pitch=gyroX, Yaw=-gyroZ, Roll=gyroY; Accel=(accelX,-accelZ,accelY).
  - This is the wrong final Deck HID mapping for the intended consumer frame.
- src/SteamInputAddonforClaw/VirtualOutput/Viiper/DualSenseEdgeDeviceStateMapper.cs
  - Currently maps Edge gyro and accel directly from the normalized X/Y/Z axes.
  - The Edge's native sensor slots are not the same coordinate convention as the Deck's historical Pitch/Yaw/Roll HID slots.
- tests/SteamInputAddonforClaw.Tests/SteamDeckDeviceStateMapperTests.cs
- tests/SteamInputAddonforClaw.Tests/DualSenseEdgeDeviceStateMapperTests.cs
  - Both currently contain assertions freezing the old permutations. Update them.
- src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml.cs
  - PhaseInstruction currently says "Keep the device level and rotate it left/right" for YAW. This confuses rotation about the world's vertical axis when the screen is flat with yaw about the controller's top/bottom (local Y) axis.

### 3.2 Consumer and virtual-report evidence

Authoritative protocol and source references:

- SDL Steam Deck driver: https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_steamdeck.c
  - SDL consumes *Deck raw gyro* and *Deck raw acceleration* slots as (X, Z, -Y), followed by ordinary unit conversions.
- SDL PS5/DualSense driver: https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_ps5.c
  - SDL consumes Sony gyro X/Y/Z and accel X/Y/Z **without a Deck-style swap/sign permutation** (though sensor calibration and unit conversion are applied).
- Canonical VIIPER Steam Deck state/wire test: https://github.com/onehoon/VIIPER/blob/main/device/steamdeck/steamdeck_test.go
  - TestMotionFieldWireOrderMatchesSteamConsumers asserts exact gyro and acceleration HID byte slots; native field names/field order are ABI/wire contracts, not direct descriptions of the final SDL coordinate axes.
- Canonical VIIPER encoder: https://github.com/onehoon/VIIPER/blob/main/device/steamdeck/inputstate.go
  - Writes AccelX/Y/Z at report bytes 24..29 and historical Pitch/Yaw/Roll gyro slots at bytes 30..35.
- CTW measured/reference correction (not a dependency):
  - https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/XboxGamingBarHelper/ControllerEmulation/Viiper/ViiperSteamDeckStateMapper.cs
  - https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/XboxGamingBarHelper/ControllerEmulation/Viiper/ViiperDualSenseEdgeStateMapper.cs
  - https://github.com/onehoon/ClawTweaks-Dev/blob/release/v0.3.98.0/Tools/DeckImuProbe/Program.cs
  - The CTW reference documents an October 19 DeckImuProbe/SDL3 motion-axis characterization and consumer behavior. Its **feature/steamcontroller** branch may still have old mapper formulas; use the *release/v0.3.98.0* corrected reference rather than blindly taking a different branch.

**Important difference:** CTW's pre-output accelerometer sample uses opposite signs to the Addon's normalized accelerometer snapshot. Do NOT copy the CTW Edge accelerometer expressions textually. Re-derive the target-specific transform using the Addon's actual normalization, below.

### 3.3 User EX/CG3EM October 21 capture (physical-source evidence)

Capture location: GoogleDrive/Addon/ClawSensorProbe_EX; MSI Claw 8 EX AI+ CG3EM, MS-1T91. User performed a dedicated StationaryBias capture and a seven-phase AxisCharacterization.

- Stationary/flat accelerometer mean: raw ~(-0.0124, -0.0896, +1.0062) g. Raw +Z is the dominant gravity-opposing component for face-up pose.
- Pitch Up/Down produces dominant raw gyro X in the relevant motion.
- Roll Left/Right exhibits strong raw gyro Z; the Yaw phase also exhibited dominant raw Z, **but it did not isolate local Y yaw**, because the recorded posture/movement was not a clean controller-upright, top-edge-up, turn-the-screen-facing-left/right motion.
- Therefore do not claim the October 21 YAW phases conclusively establish raw Y sign or a separate EX-only remap. The corrected consumer mapping is supported by SDL/VIIPER conventions, the CTW measured consumer-frame reference and the stationary-gravity evidence. User-only hardware testing after merge will check remaining directional details.

Do not bake the captured gyro bias values into this PR. Do not modify report interval, sampling rate, filtering or stale-data thresholds.

## 4. Coordinate contracts — derive once, encode per target

Shared Addon normalized motion snapshot, already implemented:

~~~text
NormGyro  = (rawGX, rawGZ, -rawGY)
NormAccel = (rawAX, rawAZ, -rawAY)
~~~

Desired **final SDL/controller-space** values for the current mapped reference frame:

~~~text
SDL Gyro  = (rawGX, rawGY, rawGZ)
SDL Accel = (rawAX, rawAY, rawAZ)
~~~

The two virtual HID targets need **different native payload permutations** to produce those same downstream SDL axes:

| Sensor | Steam Deck native report | SDL decoding of Deck report | DualSense Edge native report |
| --- | --- | --- | --- |
| Gyro X | Pitch = normX = rawX | X = Pitch = rawX | GyroX = normX = rawX |
| Gyro Y | Yaw = -normY = -rawZ | Z = -Yaw = rawZ | GyroY = -normZ = rawY |
| Gyro Z | Roll = -normZ = rawY | Y = Roll = rawY | GyroZ = normY = rawZ |
| Accel X | AccelX = normX = rawX | X = rawX | AccelX = normX = rawX |
| Accel Y | AccelY = -normY = -rawZ | Z = -AccelY = rawZ | AccelY = -normZ = rawY |
| Accel Z | AccelZ = -normZ = rawY | Y = AccelZ = rawY | AccelZ = normY = rawZ |

**Do not infer matching Deck HID and Edge HID slots from matching field names.** Deck raw slot Y and Z are consumed in swapped/sign-inverted order; Sony is not.

Existing output scales must remain unchanged:

- Steam Deck gyro: **16 counts/(deg/s)**.
- Steam Deck accel: **16384 counts/g**.
- DualSense Edge gyro: **16.384 counts/(deg/s)**.
- DualSense Edge accel: **8192 counts/g**.
- Keep existing signed Int16 rounding/clamping and finite-value checks.

## 5. Required implementation

### 5.1 Steam Deck — only the 6 native IMU assignments

In SteamDeckDeviceStateMapper.Map, replace the current six IMU field assignments inside the existing IsUsableMotion guard with:

~~~csharp
result.Pitch = EncodeI16(motion!.GyroXDegPerSecond, 16);
result.Yaw = EncodeI16(-motion.GyroYDegPerSecond, 16);
result.Roll = EncodeI16(-motion.GyroZDegPerSecond, 16);

result.AccelX = EncodeI16(motion.AccelXG, 16384);
result.AccelY = EncodeI16(-motion.AccelYG, 16384);
result.AccelZ = EncodeI16(-motion.AccelZG, 16384);
~~~

Preserve every other part of the mapper: buttons, triggers, stick axes, paddles, touchpad, quaternions, report ABI, check/neutral semantics and publisher ownership.

### 5.2 DualSense Edge — only the 6 native IMU assignments

In DualSenseEdgeDeviceStateMapper.Map, retain the current WithFreshness, complete-snapshot and finite-number guards. Replace the six assignments with:

~~~csharp
result.GyroX = EncodeI16(fresh.GyroXDegPerSecond, 16.384);
result.GyroY = EncodeI16(-fresh.GyroZDegPerSecond, 16.384);
result.GyroZ = EncodeI16(fresh.GyroYDegPerSecond, 16.384);

result.AccelX = EncodeI16(fresh.AccelXG, 8192);
result.AccelY = EncodeI16(-fresh.AccelZG, 8192);
result.AccelZ = EncodeI16(fresh.AccelYG, 8192);
~~~

In particular, **AccelX remains positive** with the Addon's current normalized MotionState; CTW's reference adapter has a different accelerometer sign basis, and applying its expressions blindly would double-invert axes. No changes to the Edge native report layout, feedback, rumble, LED, buttons or scale constants.

### 5.3 Probe UI — correct two misleading Yaw instructions

Modify only the YawLeft and YawRight PhaseInstruction strings in ClawSensorProbePage.xaml.cs. They should clearly distinguish Yaw from Roll.

Suggested wording (edit minimally for UI length):

~~~csharp
FrontendClawSensorProbePhase.YawLeft =>
    "Yaw Left: Hold the screen upright facing you, top edge up. Turn the screen to face left (left edge toward you, right edge away), then return; do not tilt it like a steering wheel.",
_ =>
    "Yaw Right: Hold the screen upright facing you, top edge up. Turn the screen to face right (right edge toward you, left edge away), then return; do not tilt it like a steering wheel."
~~~

Be precise about what the tester should physically do: rotation about the controller's **local vertical/top-edge Y axis**, not the screen-normal axis and not translation of the entire controller. Maintain the existing seven-phase labels, sequence, transition timing, CSV/report format, sensor selection and source acquisition. No new phase, wizard, graphic, calibration state or capture infrastructure.

## 6. Automated tests — must run before implementation PR completion

Modify the **existing** two mapper test files rather than creating a new testing framework.

### 6.1 Steam Deck native assignments and downstream decoded frame

For a complete normalized snapshot with gyro (1,2,3) deg/s and accel (0.5,0.25,-0.5) g, the corrected Deck native fields must be:

~~~text
Pitch=+16   Yaw=-32   Roll=-48
AccelX=+8192   AccelY=-4096   AccelZ=+8192
~~~

Update Fresh_motion_maps_normalized_axes_to_native_fields_once and any other existing assertions pinning the old Yaw/Roll, AccelY/AccelZ or sign. Update midpoint and saturation assertions based on the **new field equations**, not by simply swapping names.

Make a regression that explicitly models the SDL Steam Deck readback axes:

~~~text
Decoded gyro  = (Pitch, Roll, -Yaw)        [in equivalent physical units]
Decoded accel = (AccelX, AccelZ, -AccelY) [in equivalent physical units]
~~~

Use distinct/non-symmetric positive and negative inputs so a swap or double negation cannot pass. Account for existing per-field scaling in unit assertions.

### 6.2 Edge native assignments and SDL frame

For normalized gyro (10,-20,30) deg/s and accel (0.5,-1,2) g:

~~~text
GyroX=+164   GyroY=-492   GyroZ=-328
AccelX=+4096   AccelY=-16384   AccelZ=-8192
~~~

Update MapsFreshMotionToAllSixEdgeFieldsUsingDirectNormalizedAxes (rename to reflect the target transform), saturation tests and any existing old-axis assertions. Sony SDL reads the native X/Y/Z ordering without the Deck permutation (while calibrating/converting units).

### 6.3 Flat face-up EX regression and isolated-axes checks

Build a fixture from raw motion to normalized values through the **existing** TryNormalizeSensorAxes helper (do not modify the helper):

~~~text
Raw stationary gyro=(0,0,0)
Raw face-up accel=(0,0,+1g)
Normalized gyro=(0,0,0)
Normalized accel=(0,+1,0)

Deck HID accel=(0,-16384,0) => SDL face-up accel=(0,0,+1g)
Edge HID accel=(0,0,+8192) => SDL face-up accel=(0,0,+1g)
~~~

Also use independent X/Y/Z gyro samples with opposite signs so that both target paths yield the intended downstream SDL axes. A tilted upright-gravity case is useful, but do not invent a single exact physical yaw sign from the inconclusive EX YAW phases.

### 6.4 Preserve safety/regression tests

Existing tests must continue to cover:

- missing, stale, incomplete or non-finite motion => all six IMU output fields neutral;
- valid zero-rate gyro and real stationary gravity are not conflated with unavailable data;
- gyro/accel independent freshness and the 250 ms / 500 ms existing policy;
- signed Int16 rounding, negative values, saturation;
- no impact on buttons, triggers, sticks, M1/M2, pad and neutral non-IMU fields;
- no change to SteamDeckDeviceState/DSDeviceState layouts, ABI marshaling, native field offsets, publisher lifecycle, motion source admission or GyroscopeEnabled preference.

**Do not port CTW's synthetic nonzero neutral-gravity behavior.** The Addon's established neutral/unavailable policy remains zero IMU output.

Run the existing solution/project build and unit-test commands appropriate to the checkout; at minimum run the SteamInputAddonforClaw.Tests test project and relevant existing UI tests if the Probe text is covered. Report the actual commands and results in the implementation PR. Source-only tests and build checks are sufficient for local Codex completion.

## 7. Explicit out-of-scope work

Do not modify:

- MsiClawMotionState.TryNormalizeSensorAxes, common motion state format, source selection/reader/PID7-9 extraction;
- source polling frequencies, timestamp dedupe, sensor age, bias calibration, IMU filtering or 120 ms legacy COM stall policy;
- Full1902 owner/reconcile/recovery, PID1901↔PID1902 state, HidHide allow/hidden lists, VIIPER bus/session allocation/teardown, attach or native DLL;
- Steam Input gyro preference or profile UI, Controller/Gyroscope toggle, native QAM, overlays, rumble/feedback, acceleration units or native report ABI;
- XInput gyro-to-stick, mouse emulation, new motion axis abstractions, axis-configuration UI, new calibration machinery, or external CTW integration;
- Claw Sensor Probe CSV schema, raw sample values or timing/workflow implementation.

The fix is six assignments per target plus focused tests and two Probe UI strings. Keep one owner and one existing output mapper per target; do not introduce generic axis-transform engines, second normalization stages, wrappers, new locks or speculative race defenses.

## 8. User-only post-merge real-device validation (informational; NOT a CI/review blocker)

The **user**, not local Codex, performs physical validation after merge. Lack of a physical MSI Claw or Steam installation must NOT block the coding PR if source tests pass.

1. On the EX/CG3EM, the Claw Sensor Probe remains a **RAW physical sensor** diagnostic. Rerunning it does not by itself validate the two virtual output mappers.
2. Rerun its Axis Characterization to isolate yaw correctly: keep the Claw upright, top edge pointing upward, and turn the **facing direction** left/right like turning your head. Do not rotate it like a steering wheel or pivot the device face-up on a tabletop. Record movement in one direction and the return distinctly; check raw gyro Y is dominant if the motion is about the intended local Y axis. Repeat dedicated Roll and Pitch separately as needed.
3. For **Steam Deck virtual presentation**, verify IMU from the virtual 28DE:1205 controller using a consumer-side SDL3 sensor tool (the CTW DeckImuProbe source is a reference example, not a required runtime integration). Verify face-up = SDL +Z gravity, upright = SDL +Y gravity, and Pitch/Yaw/Roll direction/axes independently; confirm Steam Input game behavior.
4. For **DualSense Edge virtual presentation**, verify the Sony virtual controller's six IMU axes through a suitable SDL3 Sony sensor consumer, with the same face-up/upright and individually controlled rotations. The DeckImuProbe tool is Steam-Deck-specific and is NOT an Edge output validator unless separately adapted by the user.
5. If an actual device disagrees, retain the RAW Probe capture and the downstream SDL capture together, then diagnose that **specific** boundary and source/model before considering any EX-only transformation. Do not preemptively add model-specific branches on conjecture.

Do not claim real-device success in the implementation PR if no user physical run has occurred. Never make hardware retesting, USB/IP mounting, physical gyro directions or game launch a merge prerequisite.

## 9. Done criteria and PR boundary

- One targeted PR, preferably one logical implementation commit plus tests as appropriate.
- Deck native gyro/accel six-field mapping corrected using the exact existing normalized source and Deck SDL consumer convention.
- Edge native gyro/accel six-field mapping corrected using the exact existing normalized source and Sony SDL consumer convention.
- Both mappers use unchanged native scaling, finite/freshness guards, clamping and neutral policy.
- Existing mapper tests updated; meaningful raw-to-normalized-to-native-to-SDL axis and stationary-gravity regressions added.
- Two misleading Yaw Probe instructions corrected; no Probe acquisition/data-format change.
- Build and relevant automated tests pass.
- No unrequested changes to Full1902 lifecycle, physical/virtual ownership, CTW, native ABI or unrelated features.
- Actual EX/A2VM user-device verification is deferred to **post-merge user role** and cannot be used as a code-review blocker.

**Report to user after implementation:** list touched files, old/new formulas, test commands/results and any source-only limitations. Do not falsely report physical validation.
