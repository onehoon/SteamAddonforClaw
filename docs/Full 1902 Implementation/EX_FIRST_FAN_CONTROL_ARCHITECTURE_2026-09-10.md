# EX-First Fan Control Architecture

**Project:** SteamInputAddonforClaw  
**Document date:** 2026-09-10  
**Primary hardware target:** MSI Claw 8 EX AI+ / CG3EM / MS-1T91  
**Secondary target:** MSI Claw 8/7 AI+ A2VM / MS-1T52 / MS-1T42 after separate calibration  
**Status:** Architecture / calibration contract. Not yet a production implementation specification.  
**Authority:** Read together with the documents in `docs/Full 1902 Implementation/`. Controller-ownership policy in the existing Full1902 authority documents remains authoritative; this document defines an independent Device/Cooling feature and must not create a new controller authority.

---

## 1. Executive decision

The production fan-control feature should **not** be implemented as a high-rate software PWM controller and should **not** expose arbitrary fan-curve editing to normal users.

The recommended design is an **EX-first supervisory thermal controller** that uses MSI's existing EC fan-curve mechanism as the inner hardware actuator and adds only the amount of software control needed to provide simple product-level modes:

- `Firmware Auto`
- `Quiet`
- `Balanced`
- `Performance`
- `Target Temperature`

`Cooler Boost` is explicitly excluded from the product mode set. The MSI capability is retained in reverse-engineering notes only because it proves part of the firmware interface; it is not a user-facing Addon feature in this architecture.

The core topology is:

```text
Device baseline / per-game profile intent
                |
                v
        FanControlService
        (single policy owner)
                |
        +-------+--------------------+
        |                            |
        v                            v
  FanModeResolver              ThermalSupervisor
        |                    filtered temperature
        |                    hysteresis / dwell
        |                    discrete curve bias
        |                            |
        +-------------+--------------+
                      v
          CG3EMFanCalibration
          -> Fan 1 six-duty curve
          -> Fan 2 six-duty curve
                      |
                      v
      existing MSI helper / WMI transport
              Get_Fan / Set_Fan
                      |
                      v
               MSI EC / firmware
                      |
                      v
              physical Fan 1/2
```

The primary architectural principle is:

> **The Addon chooses and occasionally biases a validated EC curve; the EC remains the fast actuator.**

This gives the user target-temperature behavior and useful Quiet/Balanced/Performance presets without replacing MSI's low-level thermal loop with a fragile software servo.

---

## 2. Scope

This document covers:

1. the current Addon fan transport and diagnostic evidence;
2. Claw 8 EX / CG3EM reference values already recorded in the Addon;
3. MSI Center M reverse-engineering relevant to fan modes, six-point curves, temperatures, readback, and EC hand-back;
4. ClawTweaks behavior and lifecycle patterns that are useful as a reference;
5. the product mode model;
6. the target-temperature controller;
7. model-specific calibration;
8. startup, shutdown, restart, crash, sleep/hibernate/resume, and physical-device-loss behavior;
9. per-game profile integration;
10. UI and read-only graph design;
11. EX-first hardware validation and acceptance criteria;
12. external production/academic thermal-control references and what is actually borrowed from them.

This document intentionally does **not** redesign controller routing, HidHide authority, VIIPER ownership, WING/OEM input, or Full1902 controller presentation.

---

## 3. Non-goals

The following are not goals for v1:

- no user-editable six-point fan curve;
- no user-editable Fan1/Fan2 independent tuning;
- no `Cooler Boost` product mode;
- no direct high-rate PWM loop;
- no generic PID framework or third-party PID dependency;
- no machine-learning or self-tuning controller;
- no ADRC controller;
- no thermal-control framework intended to support arbitrary future PCs;
- no cross-process fan lock/lease framework solely to defend against theoretical races;
- no coupling of fan authority to Steam controller routing success;
- no automatic TDP reduction merely because a requested temperature cannot be reached;
- no persistence of transient controller internals such as EMA history, integral state, or current bias;
- no assumption that A2VM and CG3EM share the same calibrated curve just because they use a similar MSI WMI transport.

The goal is not to minimize LOC. The goal is **one clear fan-policy owner, one hardware-write path, one lifecycle reconciliation path, and one failure policy**.

---

## 4. Evidence and confidence model

Fan control has several source layers. They must not be collapsed into one assumed firmware contract.

### 4.1 Source classes

| Class | Meaning | How this document uses it |
|---|---|---|
| `ADDON-CODE-VERIFIED` | behavior confirmed in current SteamInputAddonforClaw source | implementation contract unless later source changes |
| `EX-REFERENCE-RECORDED` | EX / MS-1T91 values explicitly recorded by Addon diagnostics/tests | strong starting calibration evidence, still requires production hardware validation |
| `MSI-RE` | reverse engineering of MSI Center M / MSI WMI behavior | protocol/behavior evidence; do not assume every UI structure is raw EC layout |
| `CTW-SOURCE/PRODUCT` | current user fork/source tree and retained CTW research | behavior/lifecycle precedent, especially A2VM; not an EX calibration authority |
| `EXTERNAL-REFERENCE` | OpenBMC, Linux, NVIDIA, ChromiumOS, Steam Deck, papers | design rationale only |
| `PROPOSED` | Addon production architecture defined here | implement only after required calibration gates |
| `CALIBRATION-TBD` | exact values that require EX measurements | must not be hard-coded from guesswork |

### 4.2 Important separation

There are at least three different representations in the available evidence:

```text
MSI Center M UI / registry / API model
    12 logical values = CPU x6 + GPU x6

MSI new-EC WMI model
    Get_Fan(1) / Get_Fan(2)
    each returns an 8-byte logical fan block
    six mutable duty entries at [1..6]

Addon product model
    semantic mode + target + model calibration
    -> produces a validated Fan1/Fan2 curve pair
```

The 12-entry MSI Center M representation is **not proof that the low-level WMI block itself is 12 bytes**. The RE shows that Center M aggregates CPU and GPU fan values at its higher logical/configuration layer while `Get_Fan`/`Set_Fan` operate separately on target 1 and target 2.

---

## 5. Relationship to Full1902 architecture

The Full1902 project intentionally separates controller authority from independent device features. Fan control belongs to the Device side together with features such as TDP, CPU Boost, power mode, battery policy, LEDs, and telemetry.

Therefore:

```text
Controller authority / presentation
    !=
Fan control authority
```

A Steam routing transition must not be used as the owner of fan state. A fan failure must not invalidate a healthy controller route. Likewise, a controller route failure does not inherently require fan control to return to Auto.

The intended composition is:

```text
Persistent Addon runtime
    |
    +-- Controller subsystem
    |      Full1902 authority / HidHide / VIIPER / routing
    |
    +-- Device feature subsystem
           TDP
           CPU Boost
           Power Mode
           Fan Control   <--- this document
           future device features
```

The only shared inputs should be legitimate product/profile context such as the active game and selected profile. The fan subsystem must not subscribe to controller lifecycle merely as a shortcut for profile lifecycle.

---

## 6. Current Addon fan-control surface

### 6.1 Supported board identification

Current `FanProbe.cs` resolves:

```text
MS-1T42 / MS-1T52 -> A2VM
MS-1T91           -> CG3EM
other             -> Unsupported
```

This architecture makes **CG3EM / MS-1T91 the first production calibration target**.

### 6.2 Existing transport must be reused

The current Addon already has MSI helper/WMI access used by the fan probe and TDP helper infrastructure. Production fan control must build on that existing path rather than introduce a second EC/WMI stack.

Relevant operations already exercised or exposed by the existing transport/research include:

```text
Get_Fan(1) / Set_Fan(1)       Fan 1 table
Get_Fan(2) / Set_Fan(2)       Fan 2 table
Get_Temperature(...)
Get_Thermal(...)
Get_AP(1)                     fan-control ownership/status evidence
Set_Data(212 / 0xD4)          advanced/custom fan-control bit path
```

The exact existing transport API should be reused directly or minimally promoted behind the production fan feature. Do not add a generic `IECController`, `IThermalPlatform`, or similar abstraction unless a concrete second production implementation actually requires it.

### 6.3 Logical WMI fan block

Current Addon code normalizes the first eight bytes returned by the fan operation:

```text
byte 0 | duty1 duty2 duty3 duty4 duty5 duty6 | byte 7
```

The six middle bytes are the duty/speed portion modified by current diagnostic logic.

The meaning of boundary bytes `0` and `7` must be treated as **observed framing/calibration data, not guessed semantics**, until separately proven.

### 6.4 EX recorded reference

Current Addon diagnostics record this CG3EM comparison reference:

```text
EX Fan 1/2 logical:
58 | 70 74 76 78 80 84 | 94

temperature labels:
47 / 50 / 57 / 64 / 71 / 78 C

ownership: OFF in the recorded reference
Cooler Boost: OFF in the recorded reference
```

The corresponding test fixture also uses:

```text
[58, 70, 74, 76, 78, 80, 84, 94]
```

as the expected normalized eight-byte logical block.

This is the strongest current EX starting point, but production code must still capture the live EC values from the actual MS-1T91 before treating this as universal across firmware revisions.

### 6.5 Critical correction: probe mutation guard is not hardware maximum

Current `FanProbeLogic.IsSafePhysicalCurve()` accepts six duty values only when each is in the range `10..75`. That constraint exists to bound **developer diagnostic mutations**.

It must not be interpreted as a CG3EM hardware maximum because the recorded EX reference itself contains duty values `76`, `78`, `80`, and `84`.

Therefore production fan policy must use a separate model calibration/safety envelope and must not reuse the probe-only `10..75` predicate as the product clamp.

### 6.6 Existing lifecycle diagnostic is valuable

The current probe already distinguishes these resume outcomes:

```text
CUSTOM_PERSISTED
CURVE_PERSISTED_OWNERSHIP_LOST
FIRMWARE_AUTO_RESET
OTHER_STATE
READ_FAILED
```

That is exactly the evidence needed to choose a production resume policy. The production controller should reuse the *observed-state-first* idea, but not reuse the diagnostic's artificial armed `75/75/75/75/75/75` table.

---

## 7. MSI Center M reverse-engineering findings

The retained `msi_center_m_fan_control.txt` RE gives a much clearer new-EC call chain than UI-level decompilation alone.

### 7.1 New-EC fan mode behavior

The RE found three OEM modes:

```text
1 = Auto
2 = Cooler Boost
3 = Advanced/custom
```

For this Addon architecture only `Auto` and the normal custom/advanced mechanism are relevant. `Cooler Boost` remains deliberately unused.

### 7.2 Advanced curve write

MSI Center M's new-EC path performs approximately:

```text
fanTarget = CPU ? 1 : 2
current = Get_Fan(fanTarget)
current[1..6] = six configured speed values
Set_Fan(fanTarget, current)

ap = Get_AP(1)
set advanced-fan-active bit
Set_Data(212 / 0xD4, updated flag)
```

Important implications:

1. Fan 1 and Fan 2 are independently addressable.
2. The six speed values are applied against six temperature breakpoints.
3. The write is a table write, not proof of a high-rate direct PWM API.
4. Advanced/custom ownership is separately enabled after the table is written.
5. Production Addon code should preserve unmodified framing bytes by read-modify-write unless EX testing proves a stronger invariant.

### 7.3 Returning to firmware Auto

The RE shows that Center M clears the advanced-fan-active state to hand control back to firmware auto behavior.

This is a valuable product primitive:

```text
Addon controlled mode
    -> custom table + ownership ON

Firmware Auto
    -> ownership OFF / firmware controls the fan
```

The Addon should treat `Firmware Auto` as a genuine **ownership hand-back**, not as another Addon-generated curve that happens to resemble the factory curve.

### 7.4 Live defaults are firmware-derived

The RE's `Check_Fan()` path is particularly important:

- it reads an EC firmware fingerprint;
- it reads `Get_Temperature(1/2)`;
- it reads `Get_Fan(1/2)`;
- it seeds Center M's stored default/high fan and temperature values from the EC;
- a changed firmware fingerprint triggers re-seeding.

Therefore the Addon should not assume that a decompiled constant is the canonical default for every EX firmware revision.

**Production rule:** capture live board/firmware calibration where practical, and version/validate any built-in preset data by model/firmware evidence.

### 7.5 MSI Center M 12-value layer

Other retained Center M RE shows logical settings such as:

```text
Default_Fan = CPU values + GPU values
Default_Temp = CPU values + GPU values
High_Fan = CPU values + GPU values
```

with a 12-value aggregate structure. This is useful evidence for the product concept "one mode contains both CPU and GPU fan policy," but it does not replace the low-level WMI fact that Fan1 and Fan2 are written separately as eight-byte blocks.

### 7.6 Readback is part of the OEM pattern

Both Center M RE and the current Addon probe support a read-before/write/readback style of operation. Production fan writes should therefore be verified rather than assumed successful.

---

## 8. ClawTweaks reference and limits

### 8.1 Current fork/release context

The inspected user fork branch is:

```text
onehoon/ClawTweaks-Dev
release/v0.3.98.0
```

The branch head inspected on 2026-09-10 was `20a1c6380bc8d767cf2e51b339be9fc72ca12f31`.

The retained August integration research used an earlier revision of the same release line. Where retained analysis and the current branch differ, current source/behavior must be revalidated before copying assumptions into production.

### 8.2 CTW product behavior that is useful as a reference

The current CTW README describes its fan feature as:

- custom curve written directly to the EC on Lunar Lake;
- drag-to-edit curve;
- Quiet / Default / Aggressive / Custom presets;
- live applied-value readback/check;
- live CPU package-temperature indicator on the curve;
- disabling fan control hands control back to MSI firmware;
- the curve is currently global in that CTW release; per-game fan profiles are described as future work.

This gives useful product and lifecycle precedent:

1. direct EC fan policy is practical on Claw-class hardware;
2. live readback verification is useful;
3. firmware hand-back is a good disabled/Auto semantic;
4. temperature visualization is useful to the user;
5. presets are more approachable than raw fan engineering.

However, this Addon intentionally makes a different UX decision: **normal users do not edit the curve**. CTW's curve editor is a reference/testing source, not the desired Addon UI.

### 8.3 CTW is not the EX calibration authority

The inspected CTW README explicitly lists A2VM as supported and EX as possible future support. Therefore CTW's current fan behavior is primarily an **A2VM/Lunar Lake reference**, not proof of CG3EM duty limits or curve shape.

For this document:

```text
EX calibration authority:
    current Addon EX diagnostics + MSI live readback + EX hardware testing

CTW contribution:
    control UX precedent, EC-control precedent, readback precedent,
    resume/recovery behavior, and coexistence lessons
```

### 8.4 CTW resume behavior is a useful precedent

Retained CTW lifecycle research records an explicit resume handler that refreshes hardware sensors, reasserts other device settings, and invokes a **fan curve safety net**.

This proves an important real-world lifecycle fact: fan policy may need post-resume reconciliation because EC/platform state can change across sleep/hibernate.

The Addon should adopt the lifecycle lesson but not blindly copy CTW's implementation. Our existing FanProbe is already better aligned to the desired Full1902 style because it classifies actual post-resume state before deciding what it means.

### 8.5 Do not create dual fan writers

CTW and Addon should not both independently write the same fan tables while both consider themselves authoritative.

This does **not** justify a new generalized distributed fan-lock system. Use the existing supported product/integration mode to determine which product owns device settings. If CTW-integrated operation defines CTW as the device-feature owner, Addon fan control should be unavailable/inert in that mode. If a later product decision transfers fan ownership to Addon, that transfer must be explicit and singular.

The project should not defend against unsupported arbitrary third-party simultaneous EC mutation with an ever-growing cross-process authority protocol.

---

## 9. User-facing mode model

### 9.1 Final v1 mode set

```csharp
internal enum FanControlMode
{
    FirmwareAuto,
    Quiet,
    Balanced,
    Performance,
    TargetTemperature
}
```

A separate `CustomCurve` user mode should not exist in v1.

### 9.2 Meaning of each mode

#### Firmware Auto

- no Addon thermal target;
- no Addon custom curve ownership;
- hand control to MSI firmware;
- still allow read-only temperature/RPM telemetry when available.

#### Quiet

- permits a higher normal thermal equilibrium than Balanced;
- prioritizes reduced audible fan changes/noise;
- slower downward transitions and more dwell;
- **never disables safety escalation** at high temperature.

#### Balanced

- normal default candidate;
- midpoint between acoustic and thermal priorities;
- likely initial product default if hardware validation supports it.

#### Performance

- lower normal thermal target;
- earlier cooling demand;
- faster hot-side escalation;
- does not mean "maximum fan all the time."

#### Target Temperature

- user chooses a simple target temperature;
- the Addon maps that target to the same calibrated control engine used by the presets;
- user does not edit individual duty points.

### 9.3 Provisional targets

The following are **initial calibration candidates only**, not MSI specifications:

| Mode | Candidate target | Product intent |
|---|---:|---|
| Quiet | ~85 C | acoustic priority |
| Balanced | ~80 C | default equilibrium |
| Performance | ~75 C | thermal/performance priority |
| Target Temperature | provisional 70-85 C user range | user-selected equilibrium |

These values must be validated on MS-1T91 with actual sustained gaming/thermal loads before release.

---

## 10. FanControlService: single production owner

### 10.1 Responsibility

`FanControlService` should be the only production component that decides when the Addon owns the fan curve and when a curve needs to be changed.

Responsibilities:

```text
- accept Device baseline intent
- accept active per-game override intent
- resolve the effective mode
- load board-specific calibration
- read current fan/temperature/ownership state
- apply a validated curve pair
- verify readback
- run target-temperature supervisory decisions when required
- reconcile real lifecycle events
- hand back Firmware Auto on requested Auto/disable/fault
- publish read-only status for UI
```

It should **not** own:

```text
- Steam controller routing
- HidHide
- VIIPER
- TDP authority
- CPU Boost authority
- display settings
- application/game detection itself
```

Those systems supply intent/context through their existing boundaries.

### 10.2 Avoid wrapper proliferation

A likely implementation shape is sufficient:

```text
FanControlService
    -> existing IMsiClawTdpTransport / narrowly extended existing transport
    -> existing helper
```

Do not automatically create all of the following:

```text
IFanController
IFanPolicyEngine
IThermalSupervisor
IFanCurveGenerator
IFanHardwareBackend
IFanOwnershipManager
IFanRecoveryManager
```

unless a real implementation/test seam requires each one. A few pure functions/records plus one service owner are preferable.

---

## 11. Board-specific calibration model

### 11.1 Why calibration must be model-specific

The WMI calls can be common while the physical cooling plant is not. Differences may include:

- fan size;
- heatsink/heatpipe geometry;
- RPM response to the same duty value;
- acoustic resonance bands;
- firmware temperature breakpoints;
- minimum stable fan RPM;
- CPU/GPU thermal coupling;
- Panther Lake vs Lunar Lake package behavior;
- BIOS/EC firmware revisions.

Therefore:

```text
CG3EM policy semantics == A2VM policy semantics
CG3EM raw calibration != necessarily A2VM raw calibration
```

### 11.2 Proposed calibration record

Conceptually:

```csharp
internal sealed record FanBoardCalibration(
    string BoardId,
    int[] TemperatureBreakpoints,
    byte[] Fan1Baseline,
    byte[] Fan2Baseline,
    FanBiasStep[] AllowedBiasSteps,
    int MinimumTargetC,
    int MaximumTargetC,
    int HotOverrideC,
    int CriticalOverrideC,
    TimeSpan SampleInterval,
    double TemperatureFilterAlpha,
    double TargetDeadbandC,
    TimeSpan CoolDownDwell);
```

This is conceptual. Do not freeze this exact type until implementation work confirms what the existing transport already exposes.

### 11.3 Preserve the table shape

A production write should preferably:

1. `Get_Fan(target)`;
2. validate response length;
3. preserve framing bytes;
4. replace only the six validated duty positions;
5. `Set_Fan(target, table)`;
6. read back;
7. verify expected six duty positions and any required ownership bit.

This follows the OEM/RE behavior more closely than synthesizing an entire unexplained eight-byte payload from scratch.

---

## 12. EX-first baseline strategy

### 12.1 Start from the recorded EX curve, but re-read live hardware

Recorded reference:

```text
temperature: 47  50  57  64  71  78 C
EX duty:     70  74  76  78  80  84
```

This is a useful seed for the first MS-1T91 calibration run.

At production startup, however, the service should not overwrite a newer firmware's legitimate default solely because it differs from this historical reference. A live-firmware change is a realistic product lifecycle event.

Recommended policy:

```text
known supported board
    |
    +-- read live temperature breakpoints / fan tables if available
    |
    +-- identify calibration compatibility
    |
    +-- only enable Addon-controlled modes when the board/firmware evidence is accepted
```

The exact firmware-version gating strategy is a later implementation decision. Avoid a broad compatibility framework unless EX testing demonstrates the need.

### 12.2 Curve transformation should be bounded

Presets/targets should not independently invent six arbitrary values. Generate them from a validated model-specific baseline/envelope.

A useful conceptual representation is:

```text
Curve(mode, target, bias)
    = ClampToValidatedEnvelope(
          TransformAroundTarget(
              ModelBaseline,
              target,
              mode acoustic response),
          bias)
```

The transformation must preserve:

- non-decreasing cooling demand as temperature rises;
- validated minimum/maximum duty per point;
- critical/high-temperature cooling strength;
- any known Fan1/Fan2 physical differences.

### 12.3 Do not shift all points blindly

A simple `+5` or `-5` to every raw duty is not automatically a good preset algorithm. High-temperature points should generally be protected from aggressive quiet-mode reduction, while low/mid points can carry most acoustic differentiation.

Conceptually:

```text
Quiet:
    low/mid cooling reduced or delayed
    high/critical cooling converges toward safe baseline

Performance:
    low/mid cooling raised earlier
    high/critical already near validated strong cooling
```

Exact transformations are `CALIBRATION-TBD`.

---

## 13. Target Temperature controller

### 13.1 Product meaning

A target such as `80 C` should mean:

> "Use the validated fan-control envelope to keep the device around or below this thermal equilibrium when cooling authority is sufficient."

It must **not** mean:

- hold exactly 80.0 C;
- heat the device up when it is colder;
- violate fan safety limits to reduce noise;
- lower TDP without separate policy authority;
- continuously rewrite EC state at high frequency.

### 13.2 Why not direct PID -> PWM

Our known actuator is a firmware fan table plus an ownership bit. Temperature is delayed and quantized relative to workload changes. Rewriting a table every control tick would add I/O and failure surface without evidence that the hardware needs that behavior.

The preferred v1 controller is therefore:

```text
validated target-centered EC curve
            +
slow discrete feedback supervisor
```

rather than:

```text
software PID every tick -> direct fan PWM
```

### 13.3 Sensor input

The final control signal must be based on **verified EX temperature semantics**.

Candidates include:

```text
max(filtered CPU temperature, filtered GPU temperature)
```

or a platform-equivalent thermal value if MSI's fan table is demonstrably driven by a different sensor.

Do not choose `max(CPU,GPU)` merely because it is common elsewhere. Confirm `Get_Temperature`/`Get_Thermal` meaning and compare it to actual fan response on EX.

### 13.4 Filtering

A starting design is a light EMA/low-pass filter:

```text
filtered = alpha * sample + (1-alpha) * previous
```

Exact `alpha` is calibration data.

Requirements:

- reject one-sample jitter;
- retain enough responsiveness for a handheld load transition;
- reset/reseed filter state after resume or a long telemetry gap rather than treating stale pre-suspend temperature as current.

### 13.5 Deadband

A target should have a deadband so the controller does not hunt around a single degree.

Provisional example only:

```text
Target = 80 C
normal deadband ~= 79-81 C
```

This is not a fixed requirement; hardware logs should choose the final band.

### 13.6 Asymmetric response

Heating and cooling should not be treated symmetrically.

Recommended behavior:

```text
hot side:
    react sooner
    allow faster upward bias

cool side:
    require longer dwell
    step down slowly
```

For example, conceptually:

```text
<= target - 2 C for sustained dwell
    -> bias may step down by one level

target +/- deadband
    -> no change

>= target + 2 C for a short sustained period
    -> bias +1

>= target + 5 C
    -> bias +2 / stronger hot action

high safety region
    -> strongest validated curve regardless of Quiet/Target preference
```

Exact thresholds/dwell durations are calibration values, not final numbers in this document.

### 13.7 Discrete curve bias

The controller should change **one bounded scalar cooling bias**, not independently control all six points every second.

Example conceptual levels:

```text
-2  quieter/cool-side bias if allowed by the active policy
-1
 0  target/base curve
+1  additional cooling
+2  strong additional cooling
MAX strongest validated cooling curve
```

A bias transition generates a known valid curve pair. Hardware is written only if the generated pair materially differs from the currently applied pair.

This keeps the runtime state small and the behavior explainable.

### 13.8 No derivative term in v1

A derivative term is especially sensitive to noisy/quantized temperature readings. Linux `power_allocator` exposes derivative control but documents `k_d` with a default/recommended zero behavior. There is no EX evidence that derivative control is needed.

Therefore v1 should have **no D term**.

### 13.9 No integral term in v1 unless required

An integral trim can help steady-state error, but it introduces windup/saturation behavior and persisted-history questions.

V1 should first validate:

```text
base target curve + asymmetric discrete feedback
```

If EX tests show a persistent reproducible steady-state offset that cannot be handled cleanly with the bounded bias state, a small clamped integral trim may be added later with anti-windup. The integral must never be persisted across process restart/resume.

### 13.10 Saturation / unreachable target

If the strongest validated cooling state is active and temperature remains above the selected target:

```text
status = Maximum cooling / target not currently reachable
```

This is not inherently a controller fault. Ambient temperature, workload, TDP, or physical cooling capacity may make the target impossible.

Do not accumulate more controller error indefinitely and do not silently reduce TDP. TDP changes require a separate explicit product policy.

---

## 14. Optional Phase B: package-power feed-forward

Temperature is a lagging signal. Power is an earlier indication that thermal load is about to rise.

A future EX enhancement may use:

```text
CoolingDemand = BaseTargetCurve
              + TemperatureBias
              + PowerBias
```

where `PowerBias` is a small bounded anticipatory bias derived from reliable package-power telemetry.

### 14.1 Why this is attractive

Current Steam Deck `jupiter-fan-control` source lineage provides a handheld-class example of temperature-based quadratic demand plus a linear power feed-forward term (`FeedForwardQuad`) with output hysteresis and maximum-demand aggregation.

This is more relevant to our device than implementing an elaborate server-oriented adaptive PID.

### 14.2 Why it is not mandatory for v1

Add feed-forward only if hardware traces show that temperature-only control is materially late during normal EX gaming transitions.

Do not add it merely because it is theoretically superior.

A simple validated temperature supervisor is preferable if it already gives stable thermals and acceptable acoustics.

---

## 15. Fan1 / Fan2 policy

### 15.1 Hardware/API fact

Fan target 1 and fan target 2 are independently addressable at the WMI layer.

### 15.2 Product policy

The user should not see separate curve editors or independent fan targets.

Use one semantic thermal mode:

```text
Balanced / 80 C / etc.
```

and resolve it to a model-specific pair:

```text
Fan1Curve = CG3EM calibration output A
Fan2Curve = CG3EM calibration output B
```

If hardware testing shows both fans legitimately use identical tables, the calibration may share data internally. Do not encode equality as an architectural invariant just because one reference implementation writes the same values to both fans.

### 15.3 Demand aggregation

If CPU and GPU temperatures are proven to represent separate thermal demands, the system should prefer the demand requiring **more cooling**, not average the two into a falsely comfortable value. This is consistent with OpenBMC zone behavior and Steam Deck-style maximum-demand logic.

Again, exact EX sensor semantics must be validated before implementation.

---

## 16. State and persistence

### 16.1 Persist user intent only

Persist:

```text
Device baseline fan mode
Target temperature when TargetTemperature is selected
per-game fan override intent
possibly calibration/schema version if needed for migration
```

Do not persist:

```text
current fan RPM
current filtered temperature
current curve bias
last hot/cool dwell timer
integral value
last transient read failure count
```

### 16.2 Effective state

A compact runtime state is enough:

```text
RequestedDeviceMode
ActiveProfileOverride
EffectiveMode
TargetTemperature
CurrentCurveBias
LastValidTemperature
LastAppliedCurvePair
LastVerifiedOwnershipState
FaultStatus
```

This does not require an elaborate fan state machine. Most transitions are simply reconcile operations from desired intent + observed hardware state.

---

## 17. Startup and normal reconciliation

### 17.1 Startup sequence

Recommended production sequence:

```text
1. resolve supported board
2. initialize/reuse existing MSI helper transport
3. read fan1/fan2 tables
4. read temperature breakpoint/sensor data needed by calibration
5. read fan-control ownership state
6. resolve saved Device intent + any active profile context
7. if Firmware Auto requested:
       ensure firmware ownership / no Addon custom ownership
   else:
       generate validated board curve pair
       apply only if observed state differs
       verify readback/ownership
8. start slow thermal supervisor only for modes that require it
```

Preset modes may either use only a stable generated curve or also use the same slow supervisor depending on EX calibration results. The architecture permits both without changing ownership.

### 17.2 Do not blindly rewrite on every startup

If the requested custom state already exists and ownership is valid, no rewrite is required. This reduces unnecessary WMI mutations and mirrors the project's general reconcile style.

---

## 18. Shutdown, restart, and crash

### 18.1 Controlled shutdown

Recommended initial policy:

```text
controlled runtime shutdown
    -> stop supervisor
    -> hand fan control back to Firmware Auto
    -> preserve saved user intent
```

On next startup the saved intent is re-applied after observed-state reconciliation.

This gives a clear teardown path and minimizes stale software ownership after an intentional exit.

### 18.2 Process crash / forced termination

The current probe already exists partly because a custom EC table may outlive a process depending on platform state. Crash behavior is therefore a real lifecycle question, not a theoretical race.

Before production release, EX hardware validation must determine:

- whether custom tables persist after process death;
- whether ownership bit persists;
- whether firmware remains safe without the supervisor;
- what reboot does;
- whether the next Addon startup can deterministically identify and reconcile the state.

Do **not** add a watchdog/service solely in anticipation. First measure the actual failure behavior. If the EC autonomously executes a safe static curve after Addon death, that may already be an acceptable crash state until normal startup reconciliation.

---

## 19. Sleep / hibernate / resume

Sleep/hibernate/resume is explicitly in supported product lifecycle scope and must be handled.

### 19.1 Before suspend

Do not invent an automatic "always hand back Auto before suspend" rule until EX persistence tests are complete. It may cause unnecessary writes and mode churn if the EC safely persists the custom curve.

The production policy should be chosen from measured behavior.

### 19.2 After resume

Use the existing probe's observed-state philosophy:

```text
Resume
  |
  v
short hardware stabilization delay if proven necessary
  |
  v
read Fan1
read Fan2
read ownership
read fresh temperature
  |
  +-- desired state already present and owned
  |      -> no write
  |
  +-- firmware reset to Auto
  |      -> reapply effective requested mode once
  |
  +-- curve persisted but ownership was lost
  |      -> re-establish ownership only after validating curve/readback
  |
  +-- unknown / read failure
         -> do not blind-write repeatedly
         -> bounded recovery / fail-safe hand-back as appropriate
```

No epoch/barrier/generation machinery is required merely to cover instruction-level races between resume callbacks and other asynchronous work. The existing single fan owner and final observed-state reconciliation should converge to the correct state for normal lifecycle events.

---

## 20. Device loss / PnP re-enumeration

Physical device/platform availability can change during normal handheld lifecycle.

If the MSI fan transport becomes unavailable:

```text
- stop new writes
- retain user intent
- mark hardware status unavailable
- do not spin a tight retry loop
```

When a supported device/control surface becomes available again:

```text
- re-read actual hardware state
- resolve board
- revalidate calibration
- reconcile the current effective fan intent
```

Do not reuse stale Fan1/Fan2 bytes captured before the loss as if they prove the current EC state.

---

## 21. Failure and fail-safe policy

### 21.1 Temperature read failures

Suggested bounded behavior:

```text
one/few transient failures:
    keep last successfully applied autonomous EC curve
    do not immediately flap ownership

sustained/confirmed telemetry loss while TargetTemperature supervisor is required:
    stop software target supervision
    attempt a single deterministic Firmware Auto hand-back
    surface Sensor unavailable / Auto fallback
```

The exact consecutive-failure threshold is calibration/implementation detail.

### 21.2 Fan write failure

```text
Set_Fan failure
    -> bounded readback
    -> if desired table actually applied, accept observed state
    -> otherwise attempt deterministic safe hand-back
    -> no infinite retry
```

### 21.3 Ownership write/read failure

A custom table without confirmed custom-control ownership must not be reported as healthy Addon control.

If ownership cannot be established/verified:

```text
controlled mode unavailable/faulted
-> Firmware Auto hand-back if possible
```

### 21.4 Hot safety override

Presets/targets must all converge toward a validated strongest-cooling policy in the hot region.

Quiet means a higher *normal equilibrium*, not weaker emergency cooling.

The final `HotOverrideC` and `CriticalOverrideC` values are `CALIBRATION-TBD`; they must be selected from MSI/CPU/platform limits and real EX traces with reasonable margin.

---

## 22. Per-game profile integration

Fan settings should follow the same Device-baseline + per-game-override model as other device profile features.

```text
Device baseline
    Balanced

Game A profile
    Fan override = Performance

Game B profile
    Fan override = Target 78 C

Game exits
    -> restore Device baseline Balanced
```

### 22.1 Fan profile state is independent of controller routing

Game/profile detection may provide common context, but:

```text
Steam Deck routing failed
```

does not imply:

```text
cancel valid fan profile
```

and vice versa.

The feature should restore the correct Device baseline when profile scope ends regardless of controller presentation state.

### 22.2 Unsupported/default behavior

If a profile has no fan override:

```text
inherit Device baseline
```

Do not create a second default fan mode inside each game profile.

---

## 23. UI architecture

### 23.1 Placement

Preferred location:

```text
Device
  -> Cooling
```

rather than a new top-level app tab solely for fan control.

### 23.2 Main control surface

The normal user should see:

```text
Cooling

Current temperature       76 C
Fan 1                     3xxx RPM
Fan 2                     3xxx RPM
Mode                      Balanced

[ Firmware Auto ] [ Quiet ] [ Balanced ] [ Performance ] [ Target ]
```

When `Target` is selected:

```text
Target Temperature
80 C
[-]  70 ---------------- 80 ----- 85  [+]
```

Exact control style can follow current WinUI design language; architecture only requires the semantic simplicity.

### 23.3 No editable fan curve

The graph is informational, not a drag surface.

Normal users should not be expected to understand:

- six firmware breakpoints;
- Fan1 vs Fan2 relationship;
- minimum spin behavior;
- EC ownership;
- acoustic resonance;
- safe high-temperature curve shape.

Removing arbitrary curve editing also removes a large amount of validation/support burden.

### 23.4 Read-only graph

Recommended graph content:

- recent effective control temperature;
- target horizontal line for Target mode;
- optional Fan1/Fan2 RPM history;
- optional mode transition markers if useful during diagnostics;
- no draggable control points.

A separate read-only representation of the effective generated six-point curve may be useful in Developer diagnostics, but is not required in normal UI.

### 23.5 Telemetry lifetime

Do not keep a high-rate chart timer alive merely because a chart exists.

The target controller already needs slow thermal samples when active. The UI can consume those samples. Additional graph history for Firmware Auto/preset display should be collected at a modest cadence and preferably only while the relevant UI/overlay is visible unless another runtime feature already needs it.

---

## 24. Developer diagnostics

The existing bounded hardware fan probe should remain a developer/validation tool, not be turned into the normal fan service.

Useful diagnostic capabilities to retain/extend:

```text
- capture Fan1/Fan2 raw + logical tables
- capture temperature breakpoints
- capture ownership state
- capture current temperature and RPM
- safe bounded response test
- restore original tables
- firmware Auto hand-back
- suspend/resume persistence classification
- write/readback verification
```

For EX production calibration, add structured logging rather than user-facing curve editing.

---

## 25. EX calibration plan

This is the most important prerequisite before fixing final Quiet/Balanced/Performance/Target numbers.

### 25.1 Environment capture

Every calibration log should include:

```text
Product name
BaseBoard / MS-1T91
BIOS version
EC/firmware fingerprint/version if obtainable
Windows build
power source AC/DC
power mode
TDP / PL1 / PL2
ambient temperature if reasonably available
Addon build/commit
```

### 25.2 Factory Auto baseline

Collect after cold/normal boot with Firmware Auto:

```text
Get_Temperature(1)
Get_Temperature(2)
Get_Fan(1)
Get_Fan(2)
ownership/AP state
Fan1 RPM
Fan2 RPM
CPU/GPU/package temperatures
```

Confirm whether live EX matches the recorded:

```text
47/50/57/64/71/78 C
58 | 70/74/76/78/80/84 | 94
```

and whether Fan1/Fan2 are truly equal.

### 25.3 Load response runs

Minimum useful phases:

```text
A. idle stabilization 2-3 min
B. light/medium load
C. sustained normal gaming load
D. high 30-37 W class load where supported by the active EX power policy
E. abrupt load release / cooldown
F. repeated load step if needed
```

Record at approximately one-second telemetry cadence initially:

```text
timestamp
control temperature candidates
CPU temp
GPU temp if separately available
package power if available
Fan1 RPM
Fan2 RPM
current fan table/bias when it changes
mode
target
```

Do **not** call `Get_Fan` at 1 Hz forever in production merely because calibration logs do so. Calibration and production I/O budgets are separate.

### 25.4 Curve-step response

Use bounded, prevalidated test curves to determine:

- RPM response to duty changes;
- minimum stable RPM/duty;
- fan startup behavior;
- time to settle;
- whether any duty range creates undesirable hunting/noise;
- Fan1/Fan2 asymmetry;
- whether the EC itself adds hysteresis;
- how often table changes can be made safely/reliably.

OpenBMC's tuning practice of sweeping setpoints and logging actual RPM is a useful methodology reference here.

### 25.5 Lifecycle matrix

Run at least:

```text
Firmware Auto -> sleep -> resume
Balanced/custom ownership -> sleep -> resume
Target mode -> sleep -> resume
hibernate/resume
controlled Addon exit
forced Addon termination
Windows restart
EC/firmware reset scenario if safely reproducible
```

Observe rather than assume persistence.

---

## 26. Provisional tuning table

All values below are **starting hypotheses**, not release constants.

| Parameter | Initial candidate | Reason |
|---|---:|---|
| Sensor sample cadence | ~1 s | embedded/server references commonly separate slower thermal updates from fast actuator control; enough for initial EX testing |
| Control decision cadence | ~1 s | simple deterministic supervisor |
| Hardware table write cadence | only on state/bias change | avoid needless WMI traffic |
| Target range | 70-85 C | candidate user-safe/useful range pending EX validation |
| Quiet target | ~85 C | acoustic-priority hypothesis |
| Balanced target | ~80 C | equilibrium/default hypothesis |
| Performance target | ~75 C | cooling-priority hypothesis |
| Deadband | ~1-2 C | starting anti-hunt range |
| Hot response | faster than cool response | thermal safety / workload response |
| Cool-down dwell | several to tens of seconds | prevent audible fan oscillation; calibrate from EX thermal inertia |
| Derivative | none | unnecessary/noise-sensitive for v1 |
| Integral | none initially | add only if measured steady-state error justifies it |
| Bias model | small discrete levels | simple, explainable, low write frequency |

No number in this table should bypass hardware acceptance testing.

---

## 27. Validation requirements

### 27.1 Functional

- Firmware Auto reliably hands ownership back to EC.
- Quiet/Balanced/Performance each apply deterministic CG3EM curve pairs.
- Target Temperature changes the target without exposing raw curve editing.
- Fan1/Fan2 readback matches expected written duty values.
- repeated selection of the already-active mode causes no unnecessary write storm.
- per-game override and Device baseline restoration are correct.

### 27.2 Thermal behavior

- no sustained oscillatory fan hunting in common game loads;
- no rapid audible up/down cycling near target;
- hot-side response is prompt enough under abrupt load;
- quiet mode never weakens hot/critical safety behavior;
- unreachable targets saturate safely and report status instead of accumulating control error indefinitely.

### 27.3 Lifecycle

- startup from firmware Auto;
- startup after stale custom curve/ownership if actual hardware can produce it;
- sleep/resume;
- hibernate/resume;
- controlled exit;
- forced termination followed by restart;
- Windows restart;
- helper/transport operation failure;
- temporary sensor failure;
- physical/control-surface re-enumeration.

### 27.4 Performance/resource

- no busy loop;
- no high-frequency WMI table writes;
- no always-on high-rate UI telemetry requirement;
- negligible CPU impact at idle/steady state;
- log volume appropriate for normal release builds, with detailed tuning logs confined to developer/diagnostic mode.

---

## 28. Suggested phased implementation

### Phase 0 - EX evidence completion

No production UI yet.

- extend structured EX capture if needed;
- confirm Fan1/Fan2 factory tables and temperature labels;
- measure duty->RPM behavior;
- run suspend/resume and crash persistence tests;
- establish the accepted CG3EM safety envelope.

### Phase 1 - Firmware Auto + fixed validated presets

Implement the smallest production owner:

```text
Firmware Auto
Quiet
Balanced
Performance
```

with model-specific static/derived validated curves, readback, hand-back, startup/reconcile, and lifecycle handling.

Do not implement target feedback yet if the fixed modes themselves are not stable.

### Phase 2 - Target Temperature v1

Add:

- filtered temperature;
- target/deadband;
- asymmetric hot/cool dwell;
- bounded discrete curve bias;
- saturation/unreachable status;
- no D;
- no integral initially.

### Phase 3 - per-game fan override + final UI graph

Add profile override once Device-level behavior is proven.

Use read-only graphs. Do not add curve editing.

### Phase 4 - optional power feed-forward

Only if EX traces demonstrate a real benefit:

- reliable package-power source;
- bounded power bias;
- test transient temperature and acoustics against temperature-only control;
- keep write suppression and safety envelope.

### Phase 5 - A2VM calibration

Reuse the same product semantics and service owner, but add independent A2VM calibration. Do not transplant CG3EM raw values.

---

## 29. External production references

These references justify control principles; they are not drop-in implementations.

### 29.1 OpenBMC `phosphor-pid-control`

Repository: https://github.com/openbmc/phosphor-pid-control

Useful concepts:

- thermal zones can contain fan/temp/margin/stepwise controllers;
- thermal demand and fan actuator control are separable;
- multiple sensor/controller demands converge toward the maximum required cooling request;
- explicit failsafe behavior;
- thermal updates can be slower than the internal fan cycle;
- tuning documentation recommends real RPM setpoint sweeps and logging.

Applied here:

```text
separate slow thermal policy from fast hardware actuator
prefer maximum required cooling demand
have explicit fail-safe behavior
calibrate from measured fan response
```

Not applied here:

```text
full OpenBMC PID architecture
D-Bus model
server-zone abstraction
```

### 29.2 Linux thermal `power_allocator`

Documentation: https://docs.kernel.org/driver-api/thermal/power_allocator.html

Useful concepts:

- explicit desired thermal target;
- different proportional behavior for overshoot and undershoot (`k_po`, `k_pu`);
- sustainable/equilibrium baseline term;
- integral control is for slower accumulated error;
- derivative is available but default/recommended designs can use zero;
- thermal control should run at a sensible periodic cadence rather than overreact to excessively frequent updates.

Applied here:

```text
hot-side != cool-side response
small/slow correction philosophy
no derivative in v1
predictable periodic supervisor
```

### 29.3 NVIDIA `nvfancontrol` / Jetson / IGX

Example documentation: https://docs.nvidia.com/igx/user-guide/latest/SW/power-perf.html

Useful concepts:

- temperature or TMARGIN mapped to PWM/RPM profile points;
- continuous interpolation governor;
- optional PID governor;
- hysteresis around profile transitions;
- named acoustic/thermal profiles such as quiet/cool.

Applied here:

```text
profile + interpolation/curve + hysteresis is production-valid
thermal margin is a useful internal representation
profiles are better user UX than raw control coefficients
```

### 29.4 ChromiumOS EC fan control

Source: https://chromium.googlesource.com/chromiumos/platform/ec/

Relevant configuration/source documents describe:

- normally checking/updating fans around once per second;
- optional fan update-period limiting;
- board-specific percent-to-RPM behavior;
- slow-response handling for fans whose tach response lags PWM;
- custom board fan-control hooks.

Applied here:

```text
respect fan response time
avoid over-updating hardware
calibrate per board
```

### 29.5 Steam Deck `jupiter-fan-control`

Official SteamOS package index: https://steamdeck-packages.steamos.cloud/archlinux-mirror/sources/jupiter-staging/

The official package repository confirms continued `jupiter-fan-control` packaging. Current source mirrors inspected during research show a practical handheld control design using temperature-based quadratic cooling demand, optional linear power feed-forward, output hysteresis, filtering/averaging, maximum temperature override, and maximum cooling request across device demands.

Applied here:

```text
temperature curve as primary demand
optional package-power feed-forward
hysteresis/filtering
max-demand safety
handheld-scale simplicity over elaborate adaptive control
```

The mirror source is a reference implementation source, not presented as an official Valve GitHub repository.

---

## 30. Academic references

### 30.1 Kim et al., DATE 2014

**Global Fan Speed Control Considering Non-Ideal Temperature Measurements in Enterprise Servers**  
DOI: https://doi.org/10.7873/DATE.2014.289

Relevant findings include real sensor lag/quantization concerns, stability problems caused by naive control, and experiments with fixed/adaptive thermal targets. The paper is useful support for not treating temperature as a noise-free instantaneous signal.

Applied lesson:

```text
filter/dwell temperature input
avoid aggressive high-frequency correction
allow target temperature to represent an equilibrium policy
```

### 30.2 Lee & Chen, Sensors 2015

**Optimal Self-Tuning PID Controller Based on Low Power Consumption for a Server Fan Cooling System**  
DOI: https://doi.org/10.3390/s150511685

The work demonstrates that adaptive/self-tuning PID control can optimize a nonlinear fan/thermal plant and reduce cooling power.

Applied lesson:

```text
fan plant is nonlinear and tuning matters
```

Non-applied lesson:

```text
we do not need PID neural-network/self-tuning complexity for EX v1
```

### 30.3 Shin et al., ICCAD 2009

**Energy-optimal dynamic thermal management for green computing**  
DOI: https://doi.org/10.1145/1687399.1687520

The paper's useful product-level lesson is that the correct objective is not necessarily "lowest possible temperature." Cooling power/noise and processor operating point create an equilibrium tradeoff.

Applied lesson:

```text
Quiet/Balanced/Performance should represent different acceptable equilibria,
not simply low/medium/max fan percentages
```

### 30.4 Zheng et al., Control Engineering Practice 2018

**An optimized active disturbance rejection approach to fan control in server**  
DOI: https://doi.org/10.1016/j.conengprac.2018.07.003

This work emphasizes nonlinear, coupled thermal dynamics and changing workload disturbances.

Applied lesson:

```text
do not promise an exact-temperature servo from a delayed nonlinear handheld thermal system
```

Non-applied lesson:

```text
ADRC is unnecessary complexity for current product scope
```

### 30.5 ITherm 2025 adaptive-gain temperature stabilization

**Adaptive Gain Controller with State Restrictions for Fan Speed Control in Temperature Stabilization during Thermal Margin Testing**  
DOI: https://doi.org/10.1109/ITherm55376.2025.11235701

The Intel reference-platform work validates that fan-speed control can be used to stabilize CPU temperature near a thermal target/margin while controlling oscillation under varying workloads.

Applied lesson:

```text
target/margin-oriented fan control is an industry-valid objective
```

Non-applied lesson:

```text
adaptive-gain state-constrained PID complexity is not justified for our v1
```

---

## 31. Internal source/research references

### Addon repository

- `src/SteamInputAddonforClaw/Devices/MSI/Claw/FanProbe.cs`
- `src/SteamInputAddonforClaw/Devices/MSI/Claw/TdpHelperClient.cs`
- `src/SteamInputAddonforClaw.TdpHelper/TdpHelperProtocol.cs`
- `src/SteamInputAddonforClaw.TdpHelper/Program.cs`
- `tests/SteamInputAddonforClaw.Tests/MsiFanHardwareProbeTests.cs`
- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md`

### Retained MSI / Addon research

- `msi_center_m_fan_control.txt`
- `MSI_COMPLETE_RESEARCH_RESULT.md`
- `HHC_msiapcfg_analysis.txt`
- `ClawTweaks releasev0.3.98.0 ↔ SteamInputAddonforClaw 통합 재평가 최종 보고서.txt`
- `ClawTweaks_Controller_Lifecycle_HidHide_IPC_Reference_v0.3.98.0.txt`

### ClawTweaks fork

- repository: `onehoon/ClawTweaks-Dev`
- inspected branch: `release/v0.3.98.0`
- inspected current branch head on 2026-09-10: `20a1c6380bc8d767cf2e51b339be9fc72ca12f31`
- product behavior reference: branch `README.md`, especially `Fan Control (MSI Claw)`
- source tree inspected under `XboxGamingBarHelper/` including the MSI Claw device/lifecycle structure.

### HHC/MSI WMI reference

Retained research based on HHC's MSI Claw implementation confirms the MSI_ACPI WMI path and the same broad primitives (`Get_Fan`, `Set_Fan`, fan software-control bit, temperature/thermal access). Treat it as behavioral/protocol reference; implement independently through the Addon's existing transport.

---

## 32. Open questions that must be answered before final work order

1. On the target production MS-1T91 firmware, do Fan1 and Fan2 have identical factory tables?
2. Do the recorded `47/50/57/64/71/78 C` values exactly match live `Get_Temperature(1/2)` on current EX firmware?
3. What are the semantics of the fan-table boundary bytes (`58`, `94` in the recorded EX reference)? They must remain opaque until proven.
4. What physical RPM corresponds to each relevant duty value for Fan1/Fan2?
5. Is there a minimum reliable spin/start duty or RPM that needs explicit kick-start handling, or does the EC already handle it?
6. Which temperature signal best predicts the EC's own fan response on EX: CPU, GPU, max(CPU/GPU), `Get_Thermal`, or another available value?
7. Does EC firmware itself provide hysteresis for custom six-point curves, and how much?
8. Does custom table ownership persist through sleep, hibernate, process crash, and Windows restart on current EX firmware?
9. How expensive/reliable is `Set_Fan` under repeated but low-frequency production use?
10. Does a static validated target-centered curve already give acceptable target behavior without an active software bias loop?
11. If a supervisor is required, what deadband/dwell gives good acoustics on the real EX?
12. Is package-power feed-forward measurably beneficial, or unnecessary?
13. In the final CTW integration product mode, which process is the sole owner of fan/device features? Do not implement dual-writer arbitration before that policy requires it.

---

## 33. Final architecture contract

The intended EX-first production architecture is:

```text
Normal user intent
    Firmware Auto
    Quiet
    Balanced
    Performance
    Target Temperature
              |
              v
     one FanControlService
              |
      board-specific calibration
      + optional slow supervisor
              |
        validated curve pair
        Fan1 six duties
        Fan2 six duties
              |
       existing MSI transport
       Get_Fan / Set_Fan
              |
              v
          MSI EC firmware
```

The runtime must prefer:

```text
read actual state
-> reconcile once
-> verify readback
-> let EC execute the curve
-> make only meaningful bounded adjustments
-> fail back to firmware ownership when control cannot be trusted
```

over:

```text
continuous blind writes
high-rate software PWM
complex generalized PID/state-machine infrastructure
```

The design intentionally uses production precedents from OpenBMC, Linux thermal control, NVIDIA embedded platforms, ChromiumOS EC, Steam Deck fan control, MSI Center M, and CTW **without copying their complexity wholesale**.

For Claw 8 EX, the next engineering step is not another architecture abstraction. It is a **bounded MS-1T91 calibration/validation pass** that turns the currently recorded EX curve and lifecycle evidence into release-quality numeric parameters for Quiet, Balanced, Performance, and Target Temperature.
