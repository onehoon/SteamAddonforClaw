# Work Order — FAN-0: Redesign Developer Fan Hardware Probe into EX Calibration & Lifecycle Console

> **Date:** 2026-09-10  
> **Status:** Ready for implementation  
> **Reviewed baseline:** `main` at `bc0200fc75df3d12d451fe2945c73c25758952c8`  
> **Primary design authority:** `docs/Full 1902 Implementation/EX_FIRST_FAN_CONTROL_ARCHITECTURE_2026-09-10.md`  
> **Product lifecycle authority:** `docs/Full 1902 Implementation/README.md` and the referenced Full1902 authority documents  
> **Scope:** Developer-only diagnostics / calibration tooling. No production fan-control feature in this PR.

---

## 1. Goal

Replace the current legacy-style `Fan Hardware Probe` page contents with an **EX-first fan calibration and lifecycle console** while preserving the existing Developer Menu destination and reusing the existing proven MSI fan diagnostic primitives.

The purpose of FAN-0 is to gather the hardware evidence required before production fan-control presets or target-temperature control are implemented.

The target user is the developer validating a real MSI Claw device, initially:

```text
MSI Claw 8 EX AI+
CG3EM
BaseBoard: MS-1T91
```

The redesigned page must make the live hardware state visible, make each calibration action explicit, and produce structured reports suitable for deciding:

```text
- actual Fan1/Fan2 factory tables
- actual temperature breakpoints
- actual temperature sensor semantics
- duty -> RPM response
- Fan1/Fan2 asymmetry
- settling time
- ownership behavior
- Set_Fan/readback reliability
- suspend/resume persistence behavior
- safe CG3EM production calibration envelope
```

This PR is **not** the production `FanControlService` PR.

---

## 2. Required documents to read before implementation

Read these together before touching code:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/EX_FIRST_FAN_CONTROL_ARCHITECTURE_2026-09-10.md
```

Also inspect the current fan probe implementation and tests before editing:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/FanProbe.cs
src/SteamInputAddonforClaw.UI/Views/FanHardwareProbePage.xaml
src/SteamInputAddonforClaw.UI/Views/FanHardwareProbePage.xaml.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
tests/SteamInputAddonforClaw.Tests/MsiFanHardwareProbeTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
```

Do not implement from this work order alone if current `main` has materially changed.

---

## 3. Current source state verified for this work order

### 3.1 Existing Developer page

The current `FanHardwareProbePage` already exists and is wired under the Developer Menu.

Current UI groups:

```text
Basic Diagnostics
  Capture Current State
  Run Automatic Hardware Test
  Restore Firmware Auto
  Open Report Folder

Production Validation
  Run Physical Fan Response Test
  Arm Suspend / Resume Fan Test
```

The page explicitly describes itself as developer-only and not production fan control.

The menu destination, `MainNavigationPage.FanHardwareProbe`, page class, and Developer Menu navigation should remain.

### 3.2 Existing frontend contract

Current contract:

```csharp
public enum FrontendFanProbeOperation
{
    Capture,
    AutomaticTest,
    RestoreAuto,
    PhysicalResponse,
    ArmSuspendResume
}
```

and a narrow `FrontendFanProbeSnapshot` containing availability/status/device/report metadata.

The current snapshot does **not** expose enough live fan/temperature/table state to build the target calibration console without parsing text reports in the UI.

The UI must never parse report text to reconstruct hardware state.

### 3.3 Existing fan diagnostic implementation

`MsiFanHardwareProbe` already contains useful and safety-critical primitives that should be retained where valid:

```text
- model resolution:
    MS-1T42 / MS-1T52 -> A2VM
    MS-1T91           -> CG3EM

- Get_Fan read and 8-byte logical normalization
- read-modify-write preserving unowned bytes
- Set_Fan write/readback verification
- Get_AP ownership read
- advanced/custom ownership write path
- original Fan1/Fan2 capture and restore
- firmware Auto hand-back
- single-operation gate
- shutdown cleanup / bounded cancellation behavior
- report creation
- suspend/resume observation and classification
```

Do not throw these away simply because the page is being redesigned.

### 3.4 Existing tests already protect important invariants

Current `MsiFanHardwareProbeTests` verify, among other things:

```text
- first eight logical fan bytes are used from the raw response
- SetFan writes an 8-byte fan payload inside the helper/WMI envelope
- RMW preserves boundary/unowned bytes
- partial Fan1/Fan2 apply does not enable ownership
- Get_AP failure prevents guessed ownership writes
- readback mismatch fails
- restore does not blind-write ownership when state cannot be read
- physical tests restore tables and return Firmware Auto
- suspend/resume test returns to Auto
- armed test can be cancelled safely for process shutdown
- shutdown aborts later temporary test stages
```

Preserve these product-relevant safety properties.

Do not weaken existing cleanup behavior merely to make calibration code shorter.

---

## 4. Important hardware evidence already known

### 4.1 CG3EM reference recorded by current Addon

Current source records this known EX comparison reference:

```text
Temperature labels:
47 / 50 / 57 / 64 / 71 / 78 C

Fan1/Fan2 logical block reference:
58 | 70 74 76 78 80 84 | 94
```

The six middle bytes are the current mutable duty/speed positions.

The first and last bytes must continue to be treated conservatively as observed framing/calibration data unless exact semantics are proven.

### 4.2 Do not reuse the diagnostic `10..75` guard as production limits

Current `IsSafePhysicalCurve()` accepts only six duty values in `10..75` because the old physical diagnostic intentionally used a conservative bounded test range.

That is **not** the CG3EM production hardware maximum.

The EX reference itself contains:

```text
76 / 78 / 80 / 84
```

Therefore this PR must not present `75` as an EX maximum in UI, reports, or comments.

If the redesigned calibration actions retain any temporary mutation guard, name it clearly as a **probe/test guard**, not a hardware/product duty limit.

### 4.3 MSI Center M / retained RE behavior

The fan architecture document records the relevant MSI behavior:

```text
Get_Fan(1) / Set_Fan(1)
Get_Fan(2) / Set_Fan(2)
Get_Temperature(...)
Get_Thermal(...)
Get_AP(1)
advanced/custom ownership path through Set_Data(212 / 0xD4)
```

The OEM behavior is a fan-table write plus a separate ownership state, not evidence of a high-rate direct PWM API.

The redesigned Developer console should expose/measure that behavior rather than invent a new actuator model.

### 4.4 CTW is reference evidence, not EX calibration authority

The inspected CTW release line provides useful precedent for:

```text
- custom fan curves
- live EC readback verification
- live CPU package temperature display
- firmware Auto hand-back
- resume-time fan reconciliation/safety behavior
```

However its current supported fan implementation is A2VM/Lunar Lake focused.

Do not copy A2VM raw curve values into CG3EM calibration.

---

## 5. Product/scope boundary

FAN-0 is a **developer diagnostic PR only**.

It must not add:

```text
FanControlService
Quiet preset
Balanced preset
Performance preset
Target Temperature production logic
per-game fan profile persistence
normal-user Cooling card
QAM fan controls
Overlay fan controls
package-power feed-forward
software PID/PI controller
background production thermal loop
new EC driver
new generic fan/thermal framework
```

The output of this PR is **hardware evidence**, not the final fan feature.

---

## 6. Target page role

Keep the existing destination and page identity:

```text
Developer Menu
  -> Fan Hardware Probe
```

But redesign the page itself as:

```text
Fan Hardware Probe
Developer-only EX fan calibration and lifecycle diagnostics
```

The page should be organized around actual diagnostic workflow rather than the old generic `Basic Diagnostics / Production Validation` split.

Recommended sections:

```text
Device
Live State
Fan Tables
Calibration
Lifecycle
Recovery
Report
```

Do not create a second `Fan Calibration` Developer destination.

---

## 7. Target UI layout

Use the existing WinUI 3 page style and avoid introducing a custom design framework.

A target information architecture is:

```text
Fan Hardware Probe
Developer-only fan calibration and lifecycle diagnostics.
This page may temporarily write fan tables. Use only on supported MSI Claw hardware.

[Device]
Model             MSI Claw 8 EX AI+
Board             MS-1T91
Probe profile     CG3EM
BIOS / EC         <value>
Power source      AC/DC/Unknown

[Live State]
Temperature 1     xx C
Temperature 2     xx C
Fan 1 RPM         xxxx RPM / Unavailable
Fan 2 RPM         xxxx RPM / Unavailable
Ownership         Firmware Auto / Custom / Unknown
Ownership raw     0x..

[Fan Tables]
                 P1   P2   P3   P4   P5   P6
Temperature      47   50   57   64   71   78
Fan 1 Duty       70   74   76   78   80   84
Fan 2 Duty       70   74   76   78   80   84

[Refresh Live State] [Capture Baseline]

[Calibration]
[Run Fan 1 Step Test]
[Run Fan 2 Step Test]
[Run Safe EX Calibration]

[Lifecycle]
[Arm Sleep / Hibernate Resume Test]

[Recovery]
[Restore Captured Baseline]
[Return to Firmware Auto]

[Report]
Last result       ...
Report path       ...
[Open Report Folder]
```

Exact control layout can be adapted to existing app styling, but retain the above information ownership.

### 7.1 No graph dependency in FAN-0

Do not add a chart library or custom graph control in this PR.

A live graph may be useful later, but the first evidence PR only needs numeric live state, table values, and structured report output.

The required data must be reviewable from the report even if the UI is closed after the test.

---

## 8. Live State contract

The page should not require a hardware-writing operation merely to refresh state.

Add a read-only live snapshot path using the existing fan probe frontend seam.

A reasonable contract shape is:

```csharp
public sealed record FrontendFanProbeLiveState(
    int? Temperature1C,
    int? Temperature2C,
    int? Fan1Rpm,
    int? Fan2Rpm,
    byte? OwnershipRaw,
    string Ownership,
    IReadOnlyList<int> TemperatureBreakpoints,
    IReadOnlyList<byte> Fan1Logical,
    IReadOnlyList<byte> Fan2Logical);
```

and then include it in `FrontendFanProbeSnapshot`.

Exact names may differ if current transport types make another shape cleaner.

Requirements:

- failed optional telemetry must be represented as unavailable/unknown, not fabricated zero;
- Fan1/Fan2 table read failure must be visible;
- ownership unknown must stay unknown;
- the UI must not infer custom ownership from curve contents alone;
- temperature labels must be clearly distinguished from live temperature values.

Do not create a generic telemetry DTO framework for this one Developer page.

---

## 9. Operation model redesign

The old enum reflects the old page and should be replaced with operations matching the new workflow.

Recommended target set:

```csharp
public enum FrontendFanProbeOperation
{
    Refresh,
    CaptureBaseline,
    Fan1StepTest,
    Fan2StepTest,
    SafeExCalibration,
    ArmSuspendResume,
    RestoreBaseline,
    RestoreAuto
}
```

`Refresh` may instead remain `OpenFanProbeAsync()` if keeping a separate read path is cleaner. Do not add both redundant mechanisms without reason.

### 9.1 Remove/retire legacy operations

Retire the old product concepts:

```text
AutomaticTest
PhysicalResponse
```

if their behavior is fully superseded by the explicit calibration operations.

Do not keep dead enum members or hidden UI buttons solely for backward compatibility; the project is pre-release and the frontend protocol may be bumped.

### 9.2 Preserve one-operation-at-a-time behavior

Keep the existing single-operation gate.

This is a real hardware safety property, not theoretical synchronization.

Do not replace it with a generalized job scheduler/state machine.

---

## 10. Capture Baseline

`Capture Baseline` is a read-only operation and the required first step before any calibration action that may write fan state.

It should capture and report at least:

```text
Timestamp
Manufacturer / model / board
BIOS/EC/firmware string currently available
helper diagnostics currently available
Get_Fan(1) raw + logical
Get_Fan(2) raw + logical
temperature breakpoint values if obtainable
Get_Temperature(1)
Get_Temperature(2)
Get_Thermal values relevant to current diagnostics
Fan1 RPM
Fan2 RPM
Get_AP(1) raw ownership state
power source if existing code can obtain it without a new subsystem
current configured TDP/PL values only if already cheaply available through existing runtime state
```

### Baseline lifetime

The captured Fan1/Fan2 tables should remain available in the probe session for explicit `Restore Captured Baseline` and for mandatory cleanup after temporary writes.

Do not persist the baseline as a production setting/profile.

If the process exits, current shutdown cleanup rules must remain conservative.

---

## 11. Fan 1 / Fan 2 Step Tests

The goal is to measure a **small, bounded response** of each fan separately.

This is more useful than the old broad `PhysicalResponse` action because it answers whether the two fan targets behave independently and how actual RPM responds.

Each step test should:

```text
1. require a valid captured baseline or capture it immediately before mutation;
2. verify supported model;
3. read current Fan1/Fan2 and ownership state;
4. choose one bounded pre-approved duty mutation;
5. modify only the selected fan table using RMW;
6. enable custom ownership only after required writes verify;
7. sample RPM/temperature before and after;
8. record elapsed/settling observations;
9. restore captured tables;
10. return to Firmware Auto;
11. verify final readback as far as the current interface permits.
```

### 11.1 No arbitrary duty textbox

FAN-0 should **not** add a free-form duty editor.

The user must not be able to type `0`, `255`, or arbitrary six-point values through this Developer page.

Use bounded code-defined test transformations derived from the captured table.

### 11.2 CG3EM mutation rule

Do not reuse `TrySelectSafeIncrement()` blindly if it assumes `75` is an upper boundary.

The redesigned CG3EM test must distinguish:

```text
probe mutation safety bound
!=
production/hardware duty maximum
```

Choose a small delta around a validated non-critical point from the captured EX table, and never reduce the high-temperature safety point merely to produce a visible RPM change.

The exact first test point may remain conservative until real EX output is collected.

---

## 12. Safe EX Calibration operation

`Run Safe EX Calibration` should be a guided, bounded multi-stage developer diagnostic, not an exhaustive sweep of every possible duty.

Its purpose is to produce enough data to tune FAN-1 without unnecessary hardware stress.

Recommended stages:

```text
PRECHECK
  supported board
  required reads succeed
  baseline captured
  ownership readable

BASELINE SAMPLE
  temperature/RPM/table/ownership

FAN1 SMALL STEP
  bounded mutation
  immediate readback
  RPM/temperature samples
  restore

FAN2 SMALL STEP
  bounded mutation
  immediate readback
  RPM/temperature samples
  restore

OPTIONAL SECOND SAFE STEP
  only if first step succeeded and current implementation has a validated bound

FINAL RESTORE
  restore captured tables
  hand back Firmware Auto
  final readback
```

Do **not** implement a broad `10 -> 75 -> 40` style sweep simply because the old physical test had fixed bounded stages.

The new goal is EX calibration evidence, not proof that the fan can move across a wide synthetic range.

### 12.1 Abort behavior

If any required write/readback fails:

```text
abort later mutation stages
-> restore captured tables if safely possible
-> hand back Firmware Auto
-> record failure
-> no infinite retry
```

This follows existing fail-close behavior.

---

## 13. Sampling and settling data

The report should capture enough time-series data to estimate response and settling without creating a production telemetry engine.

A calibration stage may use a short local loop such as approximately one-second sampling for a bounded number of samples.

Record, where available:

```text
elapsed_ms
temperature_1_c
temperature_2_c
fan1_rpm
fan2_rpm
ownership_raw
active_test_stage
fan1_duties
fan2_duties
```

Do not introduce a permanent background timer for the Developer page.

The sampling loop exists only while a user explicitly runs a bounded calibration operation.

### 13.1 Settling calculation

If enough RPM samples are available, it is acceptable to report simple observations such as:

```text
before RPM
after/peak RPM
final sampled RPM
delta RPM
directional classification
approximate time to stable band
```

Do not add sophisticated signal-processing or PID-tuning code in FAN-0.

Raw samples are more valuable than a clever but unverified model.

---

## 14. Temperature sensor evidence

A core output of FAN-0 is determining which MSI temperature values correlate with the actual fan-control curve on EX.

Where current transport supports them, collect together:

```text
Get_Temperature(1)
Get_Temperature(2)
Get_Thermal(...)
CPU/package temperature telemetry already available to the Addon
GPU temperature telemetry already available to the Addon, if already available without adding a new provider
```

Do not add a new third-party telemetry dependency solely for this PR.

The report should make simultaneous values easy to compare.

Do not label any one sensor as the production `Control Temperature` until hardware evidence supports that conclusion.

---

## 15. Fan table display rules

Show the current logical Fan1 and Fan2 tables separately.

For the known CG3EM shape:

```text
logical[0]
logical[1..6] = six duty values
logical[7]
```

Normal Developer table presentation should emphasize `[1..6]` as duty values but still show boundary bytes somewhere in diagnostics/readout.

Example:

```text
Fan1 raw logical: 58 | 70 74 76 78 80 84 | 94
Fan2 raw logical: 58 | 70 74 76 78 80 84 | 94
```

If Fan1 and Fan2 differ, show the difference. Do not normalize them to one shared curve in the UI.

---

## 16. Ownership presentation

Show both:

```text
semantic ownership:
  Firmware Auto
  Custom
  Unknown

raw AP byte/value:
  0x..
```

The semantic label must use the same verified bit interpretation as the current diagnostic implementation.

Do not infer `Custom` from table values alone.

A table that persists while ownership is lost is a meaningful lifecycle state and must remain distinguishable.

---

## 17. Sleep / Hibernate / Resume lifecycle test

Keep and improve the existing `ArmSuspendResume` concept rather than creating a new lifecycle manager.

The current classifier already distinguishes:

```text
CUSTOM_PERSISTED
CURVE_PERSISTED_OWNERSHIP_LOST
FIRMWARE_AUTO_RESET
OTHER_STATE
READ_FAILED
```

Preserve this useful distinction.

### Target behavior

When armed:

```text
1. capture baseline;
2. install one known bounded test table/state;
3. verify it;
4. persist the report session;
5. mark UI ARMED;
6. wait for normal system suspend/hibernate/resume lifecycle;
7. after resume, wait only the existing/small required stabilization interval;
8. read actual Fan1/Fan2/ownership/temperature/RPM;
9. classify observed state;
10. restore baseline;
11. return Firmware Auto;
12. write final report.
```

### UI label

Use wording such as:

```text
Arm Sleep / Hibernate Resume Test
```

unless current Windows power event plumbing can reliably distinguish hibernate from sleep. Do not pretend it can if it cannot.

### Shutdown while armed

Preserve current shutdown cleanup semantics and tests.

Do not add epoch/barrier machinery for theoretical callback ordering.

---

## 18. Recovery controls

Two separate Developer actions are required because they have different semantics.

### Restore Captured Baseline

```text
restore the Fan1/Fan2 tables captured by the current probe session
```

This is useful after a temporary calibration stage.

If no valid baseline exists, the action should be disabled or return a clear no-baseline result. Do not fabricate a factory table from historical constants.

### Return to Firmware Auto

```text
hand fan authority back to MSI firmware using the verified ownership path
```

This is not the same as writing the historical EX factory table.

Firmware Auto should remain a genuine ownership hand-back.

---

## 19. Report format

Reports are the main output of FAN-0 and should be intentionally structured.

Continue writing human-readable text files in the existing report location unless the current code already has a stronger convention.

Recommended sections:

```text
MSI Fan Calibration Probe
Operation
Timestamp
Addon commit/build if easily available

=== ENVIRONMENT ===
Device
Board
BIOS/EC/Firmware
Helper info
Power source
TDP/PL if already available

=== BASELINE ===
Temperature labels
Temperature/Thermal sensor values
Fan1 raw/logical
Fan2 raw/logical
Fan1 RPM
Fan2 RPM
Ownership raw/semantic

=== STAGE: FAN1_STEP ===
Before state
Requested temporary mutation
Write result
Immediate readback
Samples
Directional/settling summary

=== STAGE: FAN2_STEP ===
...

=== RESUME OBSERVATION ===
...

=== CLEANUP ===
Baseline restore
Firmware Auto hand-back
Final readback

=== SUMMARY ===
Operation PASS/FAILED
Fan1 response
Fan2 response
Observed asymmetry
Ownership behavior
Resume classification when applicable
Final state
```

### 19.1 Preserve raw evidence

Where practical, include both hexadecimal and decimal values as the current Capture operation already does.

Do not log only a derived classification when raw bytes/samples are available.

### 19.2 Release log volume

Detailed calibration sampling should stay inside the generated Developer report.

Do not flood normal application logs every second during ordinary app usage.

---

## 20. Frontend transport implications

The current protocol version at the reviewed baseline is:

```text
FrontendTransportProtocol.CurrentVersion = 28
```

The target console requires richer fan-probe snapshot data and likely changes the operation enum.

If the wire-visible contract changes, bump once:

```text
28 -> 29
```

Pre-release policy: do not add a compatibility shim for v28 solely for this change.

Update together:

```text
FrontendContracts.cs
FrontendWire.cs / request payloads as needed
NamedPipeAddonFrontendClient.cs
NamedPipeAddonFrontendServer.cs
FrontendNamedPipeTransportTests.cs
```

Do not change `OverlayTransportProtocol`; FAN-0 is Main UI Developer tooling only.

---

## 21. In-process frontend behavior

Continue using the existing fan probe frontend owner in `InProcessAddonFrontendControl`.

The frontend method should:

```text
- resolve the existing fan probe session
- call the one runtime/probe operation
- return authoritative live snapshot + operation/report result
```

Do not move fan hardware logic into the WinUI page.

The page is a renderer/command surface only.

No WMI/EC access from `FanHardwareProbePage.xaml.cs`.

---

## 22. Runtime/probe implementation guidance

It is acceptable to refactor `MsiFanHardwareProbe` because its current methods reflect the old test menu.

However keep the class narrowly developer-diagnostic.

A good target is still one probe owner plus small pure helpers, for example:

```text
MsiFanHardwareProbe
  CaptureLiveState
  CaptureBaseline
  RunFanStepTest(fanTarget)
  RunSafeExCalibration
  ArmSuspendResume
  CompleteSuspendResumeAfterResume
  RestoreBaseline
  RestoreFirmwareAuto
```

Internal records for a captured snapshot/sample are reasonable.

Do not split this into multiple managers/services/interfaces unless current testing genuinely requires a seam.

### 22.1 Existing transport reuse

Reuse the existing MSI/TDP helper transport and current fan diagnostic transport extensions.

Do not add direct EC I/O or a second privileged helper path.

---

## 23. Page busy/armed behavior

Preserve the existing useful behavior that conflicting test buttons become disabled while an operation is running or the suspend/resume test is armed.

Target rules:

```text
Running calibration:
  disable all other hardware-mutating actions
  optionally allow no-op/back behavior only if cleanup remains safe

Suspend/resume ARMED:
  disable calibration and baseline mutation actions
  keep explicit cancel/recovery path available

Read-only refresh:
  do not allow it to race a hardware-mutating operation if the probe already serializes all operations
```

Keep one simple gate. Do not create a UI job queue.

---

## 24. EX-first behavior and A2VM

The page may continue to recognize A2VM because the current diagnostic supports it, but FAN-0's new **Safe EX Calibration** action is specifically for CG3EM/MS-1T91 unless equivalent A2VM bounds are explicitly preserved and tested.

Recommended UX:

```text
MS-1T91 / CG3EM:
  all EX calibration controls available

MS-1T42 / MS-1T52 / A2VM:
  live state / capture / existing safe lifecycle tools may remain available
  EX-specific calibration action disabled with clear reason

unsupported board:
  read-only unsupported state
  no fan writes
```

Do not broaden this PR into A2VM production recalibration.

---

## 25. Do not overengineer failure handling

Protect realistic hardware/lifecycle failures:

```text
- fan read failure
- temperature/RPM read failure
- Set_Fan failure
- readback mismatch
- ownership read/write failure
- partial Fan1/Fan2 application
- shutdown during an active test
- suspend/resume reset/persistence
```

Do not add machinery for theoretical instruction-level races that the existing single-operation gate and final hardware reconciliation already make harmless.

In particular, do not add:

```text
epoch counters
generation IDs
distributed fan leases
fan authority broker
retry state machine
multi-session ownership
RDP/Fast User Switching handling
```

The supported product model remains one Windows user / one interactive session.

---

## 26. Tests

Update and extend the existing fan probe tests rather than creating an entirely separate test architecture.

At minimum cover:

### Read/live snapshot

```text
- CG3EM maps from MS-1T91
- Fan1/Fan2 logical eight-byte normalization preserved
- live snapshot distinguishes unavailable telemetry from zero
- Fan1/Fan2 divergence is preserved
- ownership unknown is not guessed
```

### Baseline

```text
- baseline capture is read-only
- required table read failure prevents later write action
- RestoreBaseline uses captured bytes, not hard-coded historical EX values
- no-baseline restore is rejected safely
```

### Fan step tests

```text
- Fan1 test changes only intended Fan1 duty positions before cleanup
- Fan2 test changes only intended Fan2 duty positions before cleanup
- framing bytes are preserved
- ownership is enabled only after required table writes verify
- readback mismatch aborts later stages
- partial apply never reports success
- every completed/failed write test attempts cleanup and Auto hand-back
```

### Safe EX calibration

```text
- only CG3EM enables EX-specific calibration
- stage failure prevents later mutation stages
- bounded sampling terminates
- cleanup occurs after exceptions
- final report includes baseline, stage, cleanup, summary sections
```

### Suspend/resume

Retain coverage for:

```text
CUSTOM_PERSISTED
CURVE_PERSISTED_OWNERSHIP_LOST
FIRMWARE_AUTO_RESET
OTHER_STATE
READ_FAILED
armed cancellation
shutdown cleanup
```

### Frontend/UI contract

```text
- named pipe round-trip for the richer FanProbe snapshot
- operation enum round-trip
- protocol mismatch behavior after version bump
- UI render path tolerates unavailable optional live values
```

Do not test arbitrary theoretical interleavings simply because they can be mocked.

---

## 27. Manual EX validation checklist

After CI passes, validate on the real MS-1T91 before treating FAN-0 as complete.

Run and preserve reports for:

```text
1. cold/normal Firmware Auto -> Capture Baseline
2. Refresh Live State at idle
3. Fan 1 Step Test
4. Fan 2 Step Test
5. Safe EX Calibration
6. Return to Firmware Auto
7. Arm Sleep/Resume -> sleep -> resume
8. Arm Hibernate/Resume -> hibernate -> resume, if the same plumbing supports it
9. controlled app exit after a completed calibration
10. app shutdown/cancel while a lifecycle test is armed
```

For each run verify physically and in report:

```text
- no stuck maximum/minimum fan behavior
- final Firmware Auto hand-back works
- Fan1/Fan2 RPM remains plausible
- table readback is coherent
- no repeated write storm
- no UI hang
- report can be opened from the page
```

The calibration reports become input to the FAN-1 design constants.

---

## 28. Expected source areas

Likely files to change:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/FanProbe.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.UI/Views/FanHardwareProbePage.xaml
src/SteamInputAddonforClaw.UI/Views/FanHardwareProbePage.xaml.cs
tests/SteamInputAddonforClaw.Tests/MsiFanHardwareProbeTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
```

Possibly small supporting test/fake updates where the current fan diagnostic fake transport lives.

Do not touch production Device/QAM/Profile fan UI because it does not exist yet.

---

## 29. Acceptance criteria

FAN-0 is complete when all of the following are true:

```text
- Developer Menu still has one Fan Hardware Probe destination.
- The old generic probe layout is replaced by the calibration/lifecycle console.
- The page shows live device, temperature, fan RPM, ownership, and Fan1/Fan2 table state where available.
- Baseline capture is explicitly available and read-only.
- Fan1 and Fan2 can be tested independently with bounded code-defined steps.
- A bounded EX-specific calibration run produces structured response data.
- Existing safe RMW/readback/ownership/cleanup invariants are preserved.
- Suspend/resume lifecycle observation remains available and reports the existing meaningful classifications.
- Restore Captured Baseline and Return to Firmware Auto have distinct semantics.
- No arbitrary fan-curve editor exists.
- No production FanControlService/preset/target-temperature loop exists.
- No permanent high-rate telemetry loop exists.
- No new EC driver/helper stack exists.
- Wire-visible contract changes are covered by the frontend protocol bump and transport tests.
- Existing and new automated tests pass.
- Real MS-1T91 calibration reports can be produced and opened from the UI.
```

---

## 30. PR boundary / next PR

Do not implement FAN-1 in the same PR.

After FAN-0 is merged and real EX reports are collected, use those results to finalize:

```text
CG3EM temperature/control sensor choice
Fan1/Fan2 calibration relationship
validated duty envelope
minimum useful response step
settling/dwell behavior
factory/firmware persistence behavior
```

Then prepare **FAN-1** for the smallest production owner:

```text
Firmware Auto
Quiet
Balanced
Performance
```

Target Temperature remains a later PR after fixed validated presets are proven stable.

---

## 31. Implementation philosophy

This PR should make the existing diagnostic tool **more useful and more explicit**, not more architectural.

Prefer:

```text
one existing Developer destination
one fan probe owner
one existing MSI transport
one hardware-write gate
one cleanup path
structured evidence
```

Avoid:

```text
new managers
new background services
new generalized abstractions
new user curve editor
new fan authority system
```

The purpose of FAN-0 is to remove uncertainty before production fan control, not to implement the final controller prematurely.
