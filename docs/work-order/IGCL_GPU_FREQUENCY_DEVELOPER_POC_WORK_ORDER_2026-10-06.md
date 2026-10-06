# Work Order — Developer-Only Intel IGCL GPU Frequency Range PoC with Readback

**Date:** 2026-10-06  
**Target repository:** `onehoon/SteamAddonforClaw`  
**Baseline reviewed:** `fc28ca88efad0cc9ab711f9cdc5e3499219b087a` (`main`)  
**Scope:** Developer Menu hardware capability PoC only  
**Target hardware for validation:** MSI Claw 8 AI+ / EX-class Intel integrated GPU, specifically the current B390 test device

---

## 1. Goal

Add one small **developer-only Intel GPU Frequency probe** that answers a single implementation question before any production Device/Profile feature is designed:

> On the current B390 Intel iGPU and installed Intel graphics driver, does the official Intel Graphics Control Library (IGCL) expose a controllable GPU frequency domain, and does `ctlFrequencySetRange()` successfully change the GPU min/max range with authoritative `ctlFrequencyGetRange()` readback?

The PoC must allow the developer to:

1. read the Intel GPU frequency-domain properties;
2. read the current externally applied min/max frequency range;
3. read live frequency state (`request`, `actual`, `tdp`, `efficient`, voltage, throttle reasons);
4. apply **Max / Max** by setting both min and max to the hardware-reported non-overclock maximum;
5. immediately read the range back and clearly report whether the requested Max / Max range was verified;
6. manually refresh live state while a game is running;
7. restore the exact pre-PoC range captured before the first successful write;
8. perform a best-effort restore during normal Runtime shutdown if the PoC still owns a modified range.

This PR is evidence gathering. It is **not** the production GPU Clock feature.

---

## 2. Why this PoC is intentionally narrow

The product use case is that some GPU-heavy games can run at lower-than-expected iGPU clock while also producing lower-than-expected frame rate. If the official IGCL frequency-range control is writable on B390, a later Device/Game Profile feature may be able to raise the minimum GPU clock, including a Max/Max mode, for selected games.

Do **not** build that production policy in this PR.

A successful PoC only proves the primitive:

```text
Intel ControlLib.dll
  -> Intel integrated graphics adapter
  -> CTL_FREQ_DOMAIN_GPU
  -> ctlFrequencyGetProperties
  -> ctlFrequencyGetRange
  -> ctlFrequencySetRange(max, max)
  -> ctlFrequencyGetRange
  -> verified readback
```

Only after hardware validation should a later work order decide persistence, Device baseline, per-game overrides, AC/DC policy, Quick Settings, Overlay exposure, startup/recovery behavior, and final UX.

---

## 3. Mandatory architecture boundaries

### 3.1 Full1902 controller architecture is unchanged

This PoC is an independent developer diagnostic. It must not change or depend on:

- PID1901 / PID1902 ownership;
- DirectInput ownership;
- HidHide state;
- VIIPER lifetime or presentation;
- Steam Deck / Xbox360 presentation selection;
- WING / Game Bar suppression;
- controller recovery / reconcile;
- Center M authority transitions.

The Full1902 documents remain authoritative for those domains.

### 3.2 Use the existing elevated Runtime

The current Full1902 Runtime is already the High-integrity platform process. Do not add:

- a new elevated helper;
- `runas` from the Developer Menu;
- a service;
- a broker;
- a second privilege authority.

IGCL calls belong in the Runtime-side implementation reached through the existing frontend contract / named-pipe path.

### 3.3 Do not turn this into Device/Profile work

This PR must not modify:

- `DeviceSettings` GPU-clock persistence;
- `GameProfile` GPU-clock fields;
- RunningAppID reconciliation;
- AC/DC settings;
- production Quick Settings;
- Overlay controls;
- profile mutation policy.

There is no persistence for the PoC target range.

### 3.4 Do not copy or bundle the supplied reverse-engineered B390 implementation

A user-supplied `iGPU Clock.rar` was reviewed as behavioral/reference evidence only.

Observed reference artifacts include:

```text
B390Native.dll
  B390_Init
  B390_GetHardwareRange
  B390_GetCurrentRange
  B390_SetRange
  B390_Restore
  B390_Shutdown

iGPU Clock.dll
  uses the above native surface

iGPUClock.Elevated.dll
  supports setrange / restore command-style operations
```

The native reference searches the Intel DriverStore for `IGCLIPFProvider.dll` and contains an explicit unsupported-provider-version/no-write safety path. That demonstrates that a working B390 frequency-control path exists, but it is **not** the implementation contract for this Addon PoC.

Reference hashes from the reviewed upload:

```text
iGPU Clock.rar
SHA-256 4e785bfd3cbfc44392ee9eda6f6a9122b046da9cf286cbd9efc02150409766e4

B390Native.dll
SHA-256 fc1f52bf90b0e01dd686672656e099e06b26b7233d7624b6e6bca68972eae7e1
```

Do not:

- package `B390Native.dll`;
- package `iGPUClock.Elevated.exe`;
- search DriverStore for `IGCLIPFProvider.dll`;
- call private provider RVAs;
- reproduce provider-version-specific reverse-engineered calls;
- add a fallback to the RE path in this PR.

If official IGCL write control fails on hardware, record that result. A private-provider fallback is a separate design decision.

---

## 4. Official Intel IGCL reference contract

Use the official Intel Graphics Control Library source as the API authority:

```text
Repository:
https://github.com/intel/drivers.gpu.control-library

Pinned reviewed revision:
b6c462933502e13d1537dd5024949a51be30e63d
"Updated version to v298 (#158)"

Primary header:
include/igcl_api.h

Reference sample:
Samples/Telemetry_Samples/Sample_TelemetryAPP.cpp
```

The existing Addon already pins/references the same v298 revision in `IntelFrameLimiter.cs` and already loads the driver-supplied `ControlLib.dll` from `System32`.

The PoC must use the official frequency APIs:

```text
ctlInit
ctlClose
ctlEnumerateDevices
ctlGetDeviceProperties
ctlEnumFrequencyDomains
ctlFrequencyGetProperties
ctlFrequencyGetRange
ctlFrequencySetRange
ctlFrequencyGetState
```

`ctlFrequencyGetAvailableClocks()` is optional for this PoC and is not required for acceptance.

Important official semantics:

```text
CTL_FREQ_DOMAIN_GPU = GPU core domain

ctl_freq_properties_t.canControl
  indicates whether software may control this domain, assuming permission.

ctl_freq_properties_t.min / max
  hardware min and maximum non-overclock frequency in MHz.

ctl_freq_range_t.min / max
  current external operating range.

GetRange output < 0
  means no external limit is currently in effect for that side.

SetRange input -1
  restores the corresponding limit to its factory value.

ctl_freq_state_t.request
  current requested frequency.

ctl_freq_state_t.actual
  resolved current frequency.

ctl_freq_state_t.tdp
  maximum frequency currently supported under dynamic power/thermal conditions.

ctl_freq_state_t.throttleReasons
  reason bitmask including PL1, PL2, current, thermal, PSU, software-range, and hardware-range limits.
```

The header also states that performance/frequency APIs normally require `CTL_INIT_FLAG_USE_LEVEL_ZERO`; use the same initialization flag already used by the production `NativeIgcl` implementation.

The Intel repository states that frequency/performance APIs are currently 64-bit-only. The Addon is already an x64 application; do not add 32-bit support.

---

## 5. Existing Addon code to preserve and reuse as a pattern

Review the current production implementation before coding:

```text
src/SteamInputAddonforClaw/Profiles/Performance/IntelFrameLimiter.cs
```

Relevant existing behavior:

- dynamically loads only `%SystemRoot%\\System32\\ControlLib.dll`;
- resolves IGCL exports dynamically;
- calls `ctlInit` with Level Zero enabled;
- enumerates adapters;
- reads `ctl_device_adapter_properties_t`;
- identifies Intel vendor `0x8086`;
- carries explicit x64 ABI layout tests;
- does not package Intel's ControlLib binary.

The new PoC should follow those proven loading/marshalling conventions.

However, **do not refactor `NativeIgcl` merely to share the PoC code**. It is production FPS-limit code with its own lifecycle and compatibility policy. A small amount of duplication inside a developer-only diagnostic is preferable to changing production behavior before B390 frequency support is proven.

Also review these Developer Menu patterns:

```text
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/FanHardwareProbePage.*
src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.*
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
```

The Developer UI is a frontend. Hardware mutation must remain Runtime-owned and cross the existing frontend transport.

---

## 6. Recommended implementation shape

Keep the PoC deliberately small.

### 6.1 Add one developer-only Runtime probe

Recommended file:

```text
src/SteamInputAddonforClaw/Diagnostics/IntelGpuFrequencyProbe.cs
```

Conceptual responsibility:

```text
IntelGpuFrequencyProbe
  owns one lazy IGCL diagnostic session
  owns one ControlLib API handle
  selects the Intel integrated graphics adapter
  selects CTL_FREQ_DOMAIN_GPU
  captures the original external range before first write
  performs Max/Max write
  performs readback verification
  exposes current frequency state
  restores the original range
  best-effort restores on normal process shutdown
```

Do not create a generic GPU-control framework, provider registry, vendor abstraction, frequency manager, or future AMD/NVIDIA interface in this PR.

### 6.2 Keep one long-lived probe session once opened

Do not initialize/close IGCL on every Refresh button click.

Initialize lazily on first probe access, retain the API / adapter / frequency handles, and close them during Runtime frontend shutdown.

The production Intel FPS limiter may also have an IGCL handle open. Intel defines `CTL_RESULT_SUCCESS_STILL_OPEN_BY_ANOTHER_CALLER` as a success-with-information result. A shutdown `ctlClose()` result indicating another caller remains open is not, by itself, a GPU-frequency PoC failure.

Do not change production `NativeIgcl` close semantics in this PR.

### 6.3 Adapter selection

For this PoC:

1. enumerate adapters;
2. read adapter properties;
3. consider Intel vendor `0x8086` graphics adapters;
4. prefer/require the integrated graphics adapter flag for the handheld iGPU;
5. report adapter name and PCI device ID in the snapshot;
6. do not hardcode a guessed B390 PCI Device ID.

If a suitable Intel integrated graphics adapter cannot be identified, return `Unavailable` and do not write anything.

### 6.4 Frequency-domain selection

Enumerate frequency domains for the selected adapter and select only:

```text
type == CTL_FREQ_DOMAIN_GPU
```

Ignore Memory and Media domains.

Before enabling write actions require:

```text
GetProperties succeeded
AND type == GPU
AND canControl == true
AND hardware min/max are finite
AND hardware min <= hardware max
```

If `canControl == false`, read-only state may still be shown, but Max/Max must be unavailable.

---

## 7. Developer Menu UI

Do not create a new navigation page for this first PoC. Add one compact card directly to the existing Developer Menu.

Suggested card:

```text
Intel GPU Frequency / IGCL Probe
Official IGCL frequency-range read/write test. Developer only.

Adapter: Intel(R) ...
PCI Device: 0x....
GPU domain controllable: Yes / No

Hardware:   min xxxx MHz / max xxxx MHz
Range:      min xxxx MHz / max xxxx MHz
Request:    xxxx MHz
Actual:     xxxx MHz
TDP max:    xxxx MHz
Efficient:  xxxx MHz
Throttle:   0x........ [decoded names]

[ Refresh ] [ Set Max / Max + Readback ] [ Restore Original + Readback ]

Last operation: ...
```

No free-form frequency fields or sliders are needed in this PR.

The one write experiment is intentionally fixed:

```text
requested min = hardware max
requested max = hardware max
```

This proves the exact high-clock use case with the smallest possible UI and input-validation surface.

### Display rules

- A negative current range value from `ctlFrequencyGetRange()` must be rendered as `Factory / no external limit`, not as a negative MHz clock.
- A negative `GetState` field means `Unknown`, per the IGCL contract.
- `throttleReasons` should show the raw hexadecimal bitmask plus decoded known flags when present.
- Disable write buttons while an operation is in progress.
- Disable Max/Max when `canControl != true` or valid hardware max is unavailable.
- Disable Restore until an original range has been captured.

---

## 8. Snapshot / frontend contract

Add a narrow frontend snapshot, for example:

```csharp
public sealed record FrontendIntelGpuFrequencyProbeSnapshot(
    bool Available,
    bool CanControl,
    string AdapterName,
    uint VendorId,
    uint DeviceId,
    double? HardwareMinMhz,
    double? HardwareMaxMhz,
    double? CurrentMinMhz,
    double? CurrentMaxMhz,
    bool CurrentMinHasExternalLimit,
    bool CurrentMaxHasExternalLimit,
    double? RequestMhz,
    double? ActualMhz,
    double? TdpMhz,
    double? EfficientMhz,
    double? Voltage,
    uint ThrottleReasons,
    bool OriginalRangeCaptured,
    bool ModifiedByProbe,
    string? LastOperation,
    bool? LastOperationVerified,
    uint? LastNativeResult,
    string? FailureMessage);
```

Exact naming may follow repository conventions. Keep the information equivalent.

Use one small operation enum:

```csharp
public enum FrontendIntelGpuFrequencyProbeOperation
{
    SetMaxMax,
    RestoreOriginal
}
```

Frontend methods:

```csharp
Task<FrontendIntelGpuFrequencyProbeSnapshot> CaptureIntelGpuFrequencyProbeAsync(...)
Task<FrontendIntelGpuFrequencyProbeSnapshot> RunIntelGpuFrequencyProbeAsync(
    FrontendIntelGpuFrequencyProbeOperation operation, ...)
```

Add the corresponding named-pipe RPC methods and request payload using the existing transport conventions.

Default interface implementations must fail closed to an `Unavailable` snapshot.

---

## 9. Capture behavior

A Refresh/Capture must be read-only.

Sequence:

```text
ensure lazy IGCL session initialized
-> read selected adapter properties
-> read GPU frequency properties
-> read current range
-> read current state
-> return snapshot
```

The first successful capture may store the current range as the session's candidate original range, but it does not yet count as probe ownership.

Immediately before the **first successful write attempt**, refresh `ctlFrequencyGetRange()` once more and preserve that current range as the authoritative original range for this probe session.

This avoids restoring an old value if another legitimate setting changed between opening the Developer Menu and pressing Set Max/Max.

After the probe has successfully changed the range, do not overwrite the stored original range on later Refresh operations.

---

## 10. Set Max / Max + Readback

### 10.1 Preconditions

Refuse the write without calling `ctlFrequencySetRange()` unless:

```text
GPU domain exists
canControl == true
hardware max is finite
hardware max > 0
original range capture succeeded
```

### 10.2 Write

Construct:

```text
min = hardwareMax
max = hardwareMax
```

Call:

```text
ctlFrequencySetRange(gpuDomain, maxMaxRange)
```

### 10.3 Authoritative verification

After a successful native SetRange call, immediately call:

```text
ctlFrequencyGetRange()
```

The range readback is the authoritative PoC verification.

Report:

```text
Requested: <max> / <max>
Readback:  <min> / <max>
Verified:  true / false
Native result: 0x........
```

Allow only a tiny tolerance for floating-point representation. Do not classify substantial driver clamping or a materially different range as verified.

If SetRange returns success but GetRange fails or differs materially, the operation is **not verified**.

### 10.4 `actual` clock is observational, not the write success criterion

After range readback, also capture `ctlFrequencyGetState()`.

Do **not** require:

```text
actual == hardwareMax
```

for the write operation to pass.

The GPU can legitimately resolve below the configured range/request because of dynamic power, thermal, current, or other hardware constraints. `actual`, `request`, `tdp`, and `throttleReasons` are evidence for the later game test, not the authoritative SetRange readback.

This distinction must be visible in logs and code comments so a game running below max is not incorrectly reported as an IGCL write failure.

---

## 11. Restore Original + Readback

The probe must restore the **range that existed immediately before the first probe write**, not blindly restore hardware min/max.

Important official IGCL range semantics:

```text
GetRange side >= 0
  -> an explicit external limit is present; preserve that value.

GetRange side < 0
  -> no external limit is in effect.

SetRange side = -1
  -> restore that side to the factory value.
```

Therefore build the restore request per side:

```text
original min >= 0 ? original min : -1
original max >= 0 ? original max : -1
```

Then:

```text
ctlFrequencySetRange(restoreRange)
-> ctlFrequencyGetRange()
-> verify
```

Verification rules:

- if the original side was explicit (`>= 0`), readback must match it within tiny floating-point tolerance;
- if the original side had no external limit (`< 0`), any negative readback for that side verifies the restored no-external-limit state.

Only after verified restore should `ModifiedByProbe` become false.

Do not erase the stored original range on a failed restore; keep it available for another Restore attempt.

---

## 12. Runtime shutdown safety

This is a developer-only write diagnostic, so normal process shutdown should not intentionally leave the test range behind.

Integrate with the existing:

```text
InProcessAddonFrontendControl.BeginProcessShutdown()
```

If the probe reports that it successfully modified the range and has not subsequently verified restoration:

```text
best-effort RestoreOriginal
-> readback verify if possible
-> log result
-> close IGCL session
```

Keep this cleanup bounded and synchronous enough to fit the existing shutdown path.

Do not add:

- a persistent recovery marker;
- a Windows service;
- a watchdog;
- startup recovery;
- resume reconciliation;
- crash recovery machinery.

A hard process crash may leave the developer-applied GPU range in place. That is acceptable for this PoC and must be stated in the Developer Menu warning/log. Production lifecycle policy is deferred until official IGCL control is proven useful.

Do not restore merely because the Developer Menu is closed or the user navigates away. The developer must be able to set Max/Max, launch/run a game, and manually Refresh the state while the Runtime remains alive.

---

## 13. Logging

Use a dedicated category such as:

```text
Diagnostics.IntelGpuFrequency
```

INFO for developer-triggered write/restore operations, including:

```text
Operation
AdapterName
VendorId
DeviceId
CanControl
HardwareMinMhz
HardwareMaxMhz
OriginalMinMhz
OriginalMaxMhz
RequestedMinMhz
RequestedMaxMhz
ReadbackMinMhz
ReadbackMaxMhz
Verified
NativeResult
RequestMhz
ActualMhz
TdpMhz
ThrottleReasons
```

WARN for:

- IGCL initialization failure;
- GPU frequency domain unavailable;
- `canControl == false` when a write is requested;
- `ctlFrequencySetRange` failure;
- readback failure;
- readback mismatch;
- restore failure;
- shutdown restore failure.

Do not log per-frame or poll automatically.

---

## 14. Native marshalling requirements

Follow the exact Intel v298 x64 layout.

At minimum project:

```text
ctl_application_id_t
ctl_init_args_t
ctl_device_adapter_properties_t
ctl_freq_domain_t
ctl_freq_properties_t
ctl_freq_range_t
ctl_freq_state_t
```

Keep delegates `CallingConvention.Cdecl`, consistent with current `NativeIgcl`.

Add ABI tests for the new frequency structures. Header-derived expected x64 sizes are:

```text
ctl_freq_properties_t = 32 bytes
ctl_freq_range_t      = 24 bytes
ctl_freq_state_t      = 56 bytes
```

Also assert important offsets so an accidental bool/enum packing change is caught.

Reuse the already-proven `ctl_device_adapter_properties_t` layout shape from `NativeIgcl`; do not invent a different managed transcription without a concrete reason.

---

## 15. Tests

Add focused deterministic tests. Do not try to unit-test Intel hardware.

Required test coverage:

### 15.1 ABI

- frequency properties size/offsets match the pinned v298 header;
- frequency range size/offsets match;
- frequency state size/offsets match.

### 15.2 Pure range policy

Test helper policy for:

```text
hardware max -> Max/Max request
original explicit range -> exact restore request
original negative min/max -> -1 factory restore input
explicit restore readback verification
negative/no-external-limit restore verification
materially mismatched readback -> not verified
```

Do not introduce a large mock-native interface solely to simulate every IGCL error.

### 15.3 Frontend transport

Update existing named-pipe transport coverage for:

- Capture GPU Frequency Probe;
- Run SetMaxMax;
- Run RestoreOriginal;
- enum/string serialization if the transport's exhaustive method tests require it;
- unavailable/default behavior.

### 15.4 UI

Build/static UI tests only where current repository tests already enforce Developer Menu control names or transport availability.

Do not add automation that performs actual GPU frequency writes in CI.

---

## 16. Manual hardware validation — primary acceptance test

Perform on the actual B390 Claw.

### Stage A — read-only capability

Open Developer Menu and press Refresh.

Record:

```text
adapter name
PCI device ID
canControl
hardware min/max
current external range
request
actual
tdp
throttle reasons
```

PASS for capability discovery when:

```text
Intel integrated adapter selected
GPU frequency domain found
GetProperties succeeds
GetRange succeeds
GetState succeeds (individual unknown/negative state fields are allowed)
```

If `canControl == false`, record that as the official IGCL result and stop write testing.

### Stage B — Max/Max write/readback

Press:

```text
Set Max / Max + Readback
```

PASS for the write primitive only when:

```text
ctlFrequencySetRange succeeds
AND immediate ctlFrequencyGetRange succeeds
AND readback min == hardware max within tolerance
AND readback max == hardware max within tolerance
```

Capture the application log.

### Stage C — game observation

With Max/Max still applied:

1. launch a game previously observed to run below expected GPU clock;
2. reproduce the same scene/settings as closely as practical;
3. use Refresh in the Developer Menu between observations as practical;
4. compare default vs Max/Max:
   - `actual` GPU MHz;
   - `request` MHz;
   - `tdp` MHz;
   - throttle reasons;
   - game FPS using the user's normal measurement method.

This stage answers usefulness, not API correctness.

A valid result can be:

```text
range readback verified
but actual stays below max due PL1/PL2/thermal/current/etc.
```

That is not an implementation failure.

### Stage D — restore

Press:

```text
Restore Original + Readback
```

PASS when the pre-write external range semantics are restored and verified.

Then restart the Addon once and confirm normal startup does not apply any GPU frequency setting.

---

## 17. Decision gate after the PoC

### Case A — official IGCL works and game testing shows value

Prepare a separate production architecture/work order for:

```text
Device baseline GPU Clock
+ per-game override
+ explicit enabled state
+ min/max range policy
+ RunningAppID reconcile
+ startup/shutdown/recovery semantics
+ Device / Profile / Quick Settings UX
```

Do not implement those in this PR.

### Case B — official IGCL write/readback works but game benefit is unclear

Keep the PoC developer-only until more games are tested. Do not promote a feature based only on writable API capability.

### Case C — official IGCL exposes GPU frequency but `canControl == false` or SetRange is rejected

Record exact API results and driver version. Do not add the private-provider RE fallback in the same PR.

A separate investigation may then compare why the supplied B390-specific tool can write while public IGCL cannot.

### Case D — official frequency API/domain is unavailable

Record exact enumeration/properties evidence and stop. Do not infer that the managed ABI must be wrong unless header/layout tests or native result codes provide evidence.

---

## 18. Explicit non-goals

This PR must not add:

```text
production GPU Clock setting
Device/Profile persistence
per-game auto apply
AC/DC GPU clock values
Quick Settings GPU clock control
Overlay GPU clock control
GPU overclocking
voltage control
power-limit changes
Intel private provider calls
B390Native.dll dependency
new elevated helper
new service
new scheduler/timer polling loop
AMD/NVIDIA abstraction
crash-persistent GPU-clock ownership journal
Sleep/Resume GPU frequency reconciliation
```

Do not expand the PoC because a future production feature may eventually need these things.

---

## 19. Files expected to change

Likely minimum set:

```text
src/SteamInputAddonforClaw/Diagnostics/IntelGpuFrequencyProbe.cs                  new
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml
src/SteamInputAddonforClaw.UI/Views/DeveloperPage.xaml.cs
tests/SteamInputAddonforClaw.Tests/IntelGpuFrequencyProbeTests.cs                new
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs            if required by transport coverage
```

Do not modify `IntelFrameLimiter.cs` unless a concrete compile-time issue requires a tiny non-behavioral reuse. No production Frame Limit behavior may change in this PR.

---

## 20. Review checklist

A reviewer should reject the PR if any of the following occurs:

- a private `IGCLIPFProvider.dll` path is used instead of public ControlLib frequency APIs;
- the supplied `B390Native.dll` is bundled or copied;
- production Device/Game Profile persistence is added;
- a new elevation/helper/service architecture is added;
- controller ownership/lifecycle code is touched without necessity;
- Max/Max is reported successful solely because `ctlFrequencySetRange()` returned success without `GetRange()` verification;
- `actual` not reaching max is incorrectly classified as SetRange failure;
- original pre-PoC range is overwritten after the probe has modified the GPU;
- Restore blindly applies hardware min/max instead of restoring original external-range semantics;
- a normal Runtime shutdown intentionally leaves a known probe-owned Max/Max range without a best-effort restore;
- speculative cross-vendor abstractions are introduced.

A reviewer should **not** block the PR for theoretical instruction-level races around developer button clicks if the existing UI busy gate and one Runtime-owned probe session serialize real user operations adequately.

---

## 21. Acceptance criteria

The PR is complete when all of the following are true:

1. Developer Menu exposes one compact Intel GPU Frequency / IGCL Probe card.
2. Refresh is read-only and reports adapter, GPU domain, hardware range, current range, and frequency state.
3. The probe uses only driver-supplied `%SystemRoot%\\System32\\ControlLib.dll` and official IGCL APIs.
4. Max/Max is available only when the GPU domain reports `canControl == true`.
5. Max/Max always performs immediate `GetRange()` readback and reports verified/mismatch clearly.
6. `actual`, `request`, `tdp`, and throttle reasons are exposed for manual game comparison but are not confused with range-write verification.
7. Restore uses the exact pre-write external range semantics and verifies readback.
8. Normal Runtime shutdown performs bounded best-effort restore if the probe still owns a changed range.
9. No production profile/device setting is created.
10. No RE/private Intel provider implementation is bundled or called.
11. New IGCL structs have deterministic x64 ABI tests.
12. Frontend/named-pipe transport tests pass.
13. Existing Intel FPS limiter tests and behavior remain unchanged.
14. Full solution build/tests pass.
15. Hardware validation result can unambiguously answer whether official IGCL B390 GPU frequency range control is usable for the next production-design phase.