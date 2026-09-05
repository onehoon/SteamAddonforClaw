# Work Order — SD6A Hotfix: Fix CG3EM Claw Sensor Probe Duplicate Source Selection

> **Date:** 2026-09-05  
> **Status:** Ready for implementation  
> **Scope:** Developer-only Claw Sensor Probe source-selection hotfix  
> **Reviewed repository head:** `main` at `8f7a4ffaec270b56662a22bdcc8d2c334275c7a7`  
> **Reviewed source baseline:** probe code unchanged from `c16442747226a32cc14330e0af0fe84487149f38`  
> **Hardware evidence:** MSI Claw 8 EX AI+ / CG3EM, App `0.1.221.0`, Live Sanity session `20260905-091822-749-ff77eaadbe194d3a81d55a579b34fd01`

---

## 1. Goal

Fix the real CG3EM Claw Sensor Probe failure where valid Windows Sensor API discovery is incorrectly rejected as ambiguous because the same physical legacy sensor is observed through both:

```text
GetSensorsByCategory(SENSOR_CATEGORY_ALL)
GetSensorsByType(E83AF229-8640-4D18-A213-E22675EBB2C3)
```

and because the A2VM reference direct-type query is currently treated as if every usable XYZ candidate returned by that query were an accelerometer.

Required end state:

```text
CG3EM / WinRT unavailable
+ Legacy CategoryAll returns Physical Accelerometer + Physical Gyrometer
+ DirectType returns those same sensors plus unrelated custom sensors

→ one logical Physical Gyrometer is selected
→ one logical Physical Accelerometer is selected
→ Live Sanity starts normally
→ actual reader/cadence/XYZ characterization can proceed
```

This is a focused diagnostic hotfix.

Do **not** add production motion handling, controller-routing changes, new lifecycle authority, new protocol versions, or a generalized sensor-management framework.

---

## 2. Required reading before implementation

Read the current versions of these documents before changing code.

### 2.1 Full PID1902 authority

Follow the precedence defined by:

```text
docs/Full 1902 Implementation/README.md
```

Then read:

```text
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
```

Frozen invariant for this PR:

```text
Full1902 controller authority / PID1902 / HidHide / VIIPER / presentation lifecycle
= untouched
```

### 2.2 Gyro / SD6 diagnostic authority

Read:

```text
docs/gyro/README.md
docs/gyro/GYRO_IMU_RESEARCH_AND_SD6_DESIGN_2026-09-05.md
docs/gyro/SD6A_ENVIRONMENT_DISCOVERY_SENSOR_EVIDENCE_WORK_ORDER.md
docs/gyro/SD6A_CLAW_SENSOR_PROBE_CHARACTERIZATION_WORK_ORDER.md
docs/gyro/SD6A_CLAW_SENSOR_PROBE_PR_B_CAPTURE_MODES_AND_SUMMARIES_WORK_ORDER.md
```

Important existing design rule:

> Direct custom-type lookup is discovery/validation evidence. It must not become an unsupported device-role authority when the returned type is not role-exclusive on real hardware.

---

## 3. Real hardware failure evidence

The first CG3EM Live Sanity capture produced:

```text
SessionId   = 20260905-091822-749-ff77eaadbe194d3a81d55a579b34fd01
Device      = Claw 8 EX AI+ CG3EM Launch Pack
Model       = msi.claw.cg3em
CaptureMode = LiveSanity
StartUtc    = 2026-09-05T09:18:22.7541995Z
EndUtc      = 2026-09-05T09:18:22.866147Z
```

The CSV contains only its header, proving capture never reached live sensor publication.

Final report errors:

```text
Multiple Physical Gyrometer candidates were found.
Multiple direct-type accelerometer candidates were found.
```

This is not a hardware/driver absence case.

The same machine's Environment Discovery already proved:

```text
Legacy CategoryAll: Succeeded=True, HResult=0x00000000
Legacy DirectType:  Succeeded=True, HResult=0x00000000
WinRT Gyrometer:    Available=False
WinRT Accelerometer:Available=False
```

and exposed a real STMicro LSM6DSO IMU through Intel ISH.

---

## 4. Exact CG3EM discovery shape that must be supported

### 4.1 CategoryAll

CG3EM returned these relevant legacy sensors:

```text
Physical Accelerometer
  SensorId = 00730001-0012-0002-0000-000000000000
  Model    = LSM6DSO
  XYZ      = true/true/true

Physical Gyrometer
  SensorId = 00760001-0012-0002-0000-000000000000
  Model    = LSM6DSO
  XYZ      = true/true/true

Simple DMD
Shake Gesture
Simple Orientation
```

### 4.2 DirectType using the A2VM reference GUID

The required diagnostic query:

```text
E83AF229-8640-4D18-A213-E22675EBB2C3
```

returns on CG3EM:

```text
Physical Accelerometer
  SensorId = 00730001-0012-0002-0000-000000000000

Simple DMD

Physical Gyrometer
  SensorId = 00760001-0012-0002-0000-000000000000

Shake Gesture
```

Therefore on CG3EM this GUID is **not accelerometer-role-exclusive**.

The exact same Physical Accelerometer and Physical Gyrometer are visible through both query paths, identified by the same `SensorId`.

---

## 5. Current root cause

Current `ClawSensorProbeSensorApi.Discover()` appends the two legacy query projections independently:

```csharp
candidates.AddRange(categoryAll.Candidates.Select(ToProbeCandidate));
candidates.AddRange(direct.Candidates.Select(x =>
    ToProbeCandidate(x) with
    {
        IsDirectTypeMatch = true,
        SelectionReason = "Matched a direct GetSensorsByType lookup."
    }));
```

This means one physical sensor can appear twice in the selection input.

### 5.1 Gyroscope failure

Current gyroscope selection counts occurrences instead of logical physical legacy sensor identity:

```csharp
var legacy = sensors
    .Where(x =>
        x.Backend == ClawSensorProbeBackend.LegacySensorApi &&
        string.Equals(x.FriendlyName, "Physical Gyrometer", StringComparison.OrdinalIgnoreCase) &&
        IsUsableLegacyCandidate(x))
    .ToArray();
```

On CG3EM this sees:

```text
Physical Gyrometer / SensorId G / CategoryAll
Physical Gyrometer / SensorId G / DirectType
```

as two candidates even though both rows describe the same physical sensor.

### 5.2 Accelerometer failure

Current direct-tier accelerometer selection is too broad:

```csharp
var direct = sensors
    .Where(x =>
        x.Backend == ClawSensorProbeBackend.LegacySensorApi &&
        x.IsDirectTypeMatch &&
        IsUsableLegacyCandidate(x))
    .ToArray();
```

On CG3EM both of these pass that filter:

```text
Physical Accelerometer / XYZ=true,true,true
Physical Gyrometer     / XYZ=true,true,true
```

so the direct tier incorrectly reports accelerometer ambiguity.

The direct query proves type-query membership. It does **not** prove the returned sensor's application role.

---

## 6. Required implementation

Implement the smallest clear correction around the existing `ClawSensorDiscovery` selection authority.

Preferred design:

```text
raw query evidence
    ↓
selection-only logical candidate projection
    ↓
existing role-specific selection policy
```

Do not create another manager/service/registry.

### 6.1 Deduplicate legacy query occurrences by physical SensorId for selection

Before role selection, construct a logical selection projection where duplicate legacy rows with the same exact `SensorId` are one source.

Required semantics:

```text
LegacySensorApi + same valid SensorId
→ same logical sensor for selection
```

Use a simple case-insensitive exact SensorId comparison.

Do **not** collapse entries whose SensorId is empty, null-equivalent, or `Unavailable` into one source merely because the placeholder matches.

WinRT candidates are not part of this legacy dedupe rule.

### 6.2 Preserve direct-query validation on the logical candidate

If one occurrence of the same legacy SensorId came from DirectType:

```text
logicalCandidate.IsDirectTypeMatch = true
```

If CategoryAll also returned that SensorId, keep the broad-enumeration candidate as the canonical metadata projection and overlay only the direct-match fact needed for selection.

Do not build a generalized metadata merge engine.

The raw per-query evidence already preserves query-specific metadata and DevicePath values.

Conceptually:

```csharp
private static IReadOnlyList<ClawSensorProbeCandidate> BuildSelectionCandidates(
    IReadOnlyList<ClawSensorProbeCandidate> sensors)
{
    // WinRT: keep as-is.
    // Legacy with usable SensorId: one logical row per SensorId.
    // If any duplicate row was DirectType, propagate IsDirectTypeMatch=true.
    // Prefer the non-direct / CategoryAll row as canonical when both exist.
}
```

Equivalent code is acceptable. Keep it local to the current discovery/selection owner.

### 6.3 Make `ClawSensorDiscovery.Sensors` the logical candidate projection

Preferred final contract:

```text
ClawSensorDiscovery.Sensors
= deduplicated logical source projection used for selection
```

The finalized report still preserves full raw evidence through:

```text
Discovery.LegacyCategoryAll
Discovery.LegacyDirectTypeQueries
Discovery.WinRtGyrometer
Discovery.WinRtAccelerometer
```

Therefore removing duplicate query occurrences from `SensorDiscovery` does not lose diagnostic evidence.

Expected CG3EM logical legacy projection after merge:

```text
Physical Accelerometer  // IsDirectTypeMatch=true
Simple DMD               // IsDirectTypeMatch=true
Physical Gyrometer       // IsDirectTypeMatch=true
Shake Gesture            // IsDirectTypeMatch=true
Simple Orientation       // broad-only
```

### 6.4 Restrict the direct accelerometer tier to the accelerometer role

The direct accelerometer tier must require exact role identity in addition to direct-query validation:

```csharp
var direct = sensors
    .Where(x =>
        x.Backend == ClawSensorProbeBackend.LegacySensorApi &&
        x.IsDirectTypeMatch &&
        string.Equals(
            x.FriendlyName,
            "Physical Accelerometer",
            StringComparison.OrdinalIgnoreCase) &&
        IsUsableLegacyCandidate(x))
    .ToArray();
```

Equivalent minimal code is acceptable.

This is not a general friendly-name-only selection regression.

The actual policy becomes:

```text
direct query validated this exact physical sensor
AND
sensor role is Physical Accelerometer
AND
required XYZ fields are supported
AND
state is usable
AND
logical candidate is unique
```

### 6.5 Keep real ambiguity fail-close behavior

Do not weaken the current safety rule.

These must still fail closed:

```text
Physical Gyrometer / SensorId G1
Physical Gyrometer / SensorId G2
```

and:

```text
Direct Physical Accelerometer / SensorId A1
Direct Physical Accelerometer / SensorId A2
```

The hotfix only removes false ambiguity caused by:

```text
same sensor / same SensorId / multiple query paths
```

or by a role-mismatched sensor being returned from the same direct type query.

---

## 7. Files expected to change

Keep the diff focused.

Expected primary files:

```text
src/SteamInputAddonforClaw/Diagnostics/ClawSensorProbe/ClawSensorProbeContracts.cs
tests/SteamInputAddonforClaw.Tests/ClawSensorProbe/ClawSensorProbeTests.cs
```

Change `ClawSensorProbeSensorApi.cs` only if current implementation makes the logical merge significantly clearer there.

Do not move responsibility across several new files merely for this fix.

Target size:

```text
one small PR
roughly <= 300 changed/new LOC where practical
```

---

## 8. Required regression tests

Add focused tests based on the real CG3EM evidence.

### 8.1 Exact CG3EM duplicate-query regression

Construct a fixture equivalent to:

```text
CategoryAll:
  Physical Accelerometer / A
  Simple DMD              / D
  Physical Gyrometer      / G
  Shake Gesture           / S
  Simple Orientation      / O

DirectType:
  Physical Accelerometer / A
  Simple DMD              / D
  Physical Gyrometer      / G
  Shake Gesture           / S
```

All relevant Physical sensor rows should expose XYZ=true/true/true exactly as needed for the real case.

Expected:

```text
IsValid       = true
Gyroscope     = G
Accelerometer = A
```

### 8.2 Logical projection dedupe

Verify the same CG3EM fixture does not keep duplicate logical sensor rows merely because the same SensorId appeared through two queries.

Expected logical count for the exact fixture above:

```text
5
```

Verify at least:

```text
Physical Accelerometer.IsDirectTypeMatch == true
Physical Gyrometer.IsDirectTypeMatch     == true
```

### 8.3 Role-mismatched direct candidate is not accelerometer ambiguity

Fixture:

```text
Direct Physical Accelerometer / A / XYZ=true,true,true
Direct Physical Gyrometer     / G / XYZ=true,true,true
```

Expected:

```text
Accelerometer = A
```

The gyrometer must not count toward the direct accelerometer tier.

### 8.4 Direct-only fallback remains valid

Cover the existing intended recovery case where CategoryAll fails/is absent but DirectType still returns usable physical sources.

At minimum verify a direct-only `Physical Accelerometer` remains selectable when it is the unique usable role-appropriate direct candidate.

Do not accidentally require a matching CategoryAll row.

### 8.5 Real distinct gyro ambiguity still fails closed

Fixture:

```text
Physical Gyrometer / G1
Physical Gyrometer / G2
Physical Accelerometer / A
```

Expected:

```text
Gyroscope = null
IsValid   = false
error contains Multiple Physical Gyrometer candidates
```

### 8.6 Real distinct direct accelerometer ambiguity still fails closed

Fixture:

```text
Direct Physical Accelerometer / A1
Direct Physical Accelerometer / A2
Physical Gyrometer / G
```

Expected:

```text
Accelerometer = null
IsValid       = false
error contains Multiple direct-type accelerometer candidates
```

### 8.7 Existing backend precedence unchanged

Existing tests proving:

```text
WinRT Gyrometer precedence
WinRT Accelerometer precedence
mixed WinRT/Legacy backend support
explicit unusable-state rejection
missing XYZ rejection
```

must continue to pass unchanged unless a test assertion was specifically encoding the false duplicate-query behavior.

---

## 9. Report / protocol contract

This hotfix must not introduce a new report schema or frontend protocol.

Keep:

```text
Claw Sensor Report SchemaVersion = 2
FrontendTransportProtocol = current main value
existing Start(mode) / Stop / Previous / Next RPC shape
existing Live / Axis / Bias modes
existing CSV columns
existing timing/statistics projection
```

No wire-format bump is justified.

The raw query blocks remain the detailed evidence authority:

```text
LegacyCategoryAll
LegacyDirectTypeQueries
WinRtGyrometer
WinRtAccelerometer
```

`SensorDiscovery` may become cleaner because it represents logical sources rather than duplicate query observations.

---

## 10. Lifecycle / Full1902 non-goals

Do not touch:

```text
PID1901 ↔ PID1902 routing
Center M Enabled/Disabled authority
HidHide applications/hidden devices
VIIPER ownership
Steam Deck/X360 presentation switching
publisher cadence
sleep/resume controller reconciliation
PnP controller ownership recovery
runtime startup task policy
frontend lifetime authority
```

Also do not add diagnostic auto-recovery machinery for sensor loss in this hotfix.

This PR fixes discovery selection only.

---

## 11. Explicit non-goals

Do not implement any of the following here:

```text
production MotionState
Steam Deck IMU publication
250 Hz motion resampling
axis transform
quaternion generation
sensor fusion
synthetic gravity
zero-rate bias subtraction
bias calibration persistence
A2VM axis constants copied to CG3EM
A2VM hard-coded gyro bias
new sensor manager/service/registry
new retry/epoch/lock/barrier framework
sensor role inference from VID/PID alone
```

The hardware characterization phase has not yet frozen the CG3EM production source contract.

---

## 12. Implementation guidance / overengineering guard

This is a concrete hardware-reproduced defect, but the correction is small.

Do not turn it into a generalized sensor identity system.

Supported product fact demonstrated by CG3EM:

```text
one Windows legacy sensor
can be returned by multiple ISensorManager discovery queries
```

The sufficient identity for this diagnostic is the already-exposed legacy `SensorId`.

Do not add:

```text
sensor authority manager
cross-backend identity resolver
PnP topology graph owner
persistent sensor registry
multi-session arbitration
query provenance class hierarchy
```

unless current code makes one tiny local helper impossible.

Prefer:

```text
one local selection projection
one role-correct filter
focused regression tests
```

---

## 13. Verification

Before opening the PR:

### 13.1 Targeted tests

Run the Claw Sensor Probe test class/filter and verify all new CG3EM regression cases pass.

Example equivalent command:

```powershell
dotnet test SteamInputAddonforClaw.slnx -c Release --filter "FullyQualifiedName~ClawSensorProbeTests"
```

Use the repository's current supported test invocation if the solution/test runner has moved.

### 13.2 Full suite

Run:

```powershell
dotnet test SteamInputAddonforClaw.slnx -c Release
```

Expected:

```text
0 failed
0 build errors
no new warnings attributable to this change
```

### 13.3 Diff inspection

Confirm the PR contains no unrelated changes to:

```text
controller routing
Full1902 ownership
frontend protocols
report schema
UI layout
motion publication
```

---

## 14. Hardware acceptance after merge

Hardware validation is expected after the code fix and is **not a PR merge blocker** if the deterministic regression tests and full suite pass.

On Claw 8 EX AI+ / CG3EM:

```text
Developer → Claw Sensor Probe → Live Sanity
```

Expected discovery result:

```text
Gyro:
  Backend      = LegacySensorApi
  FriendlyName = Physical Gyrometer
  Model        = LSM6DSO
  SensorId     = 00760001-0012-0002-0000-000000000000

Accel:
  Backend      = LegacySensorApi
  FriendlyName = Physical Accelerometer
  Model        = LSM6DSO
  SensorId     = 00730001-0012-0002-0000-000000000000
```

Expected session behavior:

```text
capture reaches Recording
CSV contains actual GYRO / ACCEL sample rows
no false "Multiple ... candidates" error
```

After that succeeds, continue the existing SD6A hardware plan:

```text
Live Sanity
→ Axis Characterization
→ Stationary Bias flat
→ Stationary Bias tilted
→ restart / sleep-resume fresh-session checks
```

Then compare A2VM ↔ CG3EM evidence and freeze the measured CG3EM production source contract before SD6 production motion work.

---

## 15. PR completion criteria

The PR is complete when all of the following are true:

- [ ] Same Legacy `SensorId` returned by CategoryAll + DirectType is treated as one logical source for selection.
- [ ] DirectType membership is propagated to the logical source without losing raw per-query evidence.
- [ ] Direct accelerometer tier only counts role-appropriate `Physical Accelerometer` candidates.
- [ ] CG3EM regression fixture selects one LSM6DSO gyro and one LSM6DSO accelerometer.
- [ ] Different SensorIds still preserve true ambiguity fail-close behavior.
- [ ] WinRT precedence and mixed-backend behavior remain unchanged.
- [ ] Schema v2 and frontend protocol remain unchanged.
- [ ] No Full1902 / HidHide / VIIPER / controller-routing code is modified.
- [ ] Targeted Claw Sensor Probe tests pass.
- [ ] Full Release test suite passes.
- [ ] Final diff is limited to this diagnostic selection hotfix and tests.

---

## 16. Suggested PR title

```text
SD6A: Fix CG3EM duplicate legacy sensor selection
```
