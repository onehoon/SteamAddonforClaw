# Work Order — PR1: Production Intel Minimum GPU Clock Core + Dynamic IGCL Clock Capability

> **Date:** 2026-10-07  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `main@4ed7a34d063276e96b7065a52a238c50bdfdb9d8`  
> **Product architecture:** standalone Full1902  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and its active precedence chain  
> **Previous hardware PoC:** `docs/work-order/IGCL_GPU_FREQUENCY_DEVELOPER_POC_WORK_ORDER_2026-10-06.md` / merged PR #693  
> **Official Intel API baseline:** Intel Graphics Control Library `igcl_api.h`, commit `b6c462933502e13d1537dd5024949a51be30e63d`  
> **Intel upstream status checked 2026-10-07:** current `intel/drivers.gpu.control-library:master` is the same commit `b6c462933502e13d1537dd5024949a51be30e63d`  
> **Scope:** production core only — official IGCL frequency capability, dynamic selectable-clock policy, minimum-frequency ownership/restore primitive, durable ownership evidence, deterministic tests, and read-only startup discovery  
> **Explicitly deferred:** Device/Profile persistence and UI, AC/DC selection, active-game override, Quick Settings/Overlay exposure, and production reconcile wiring

---

## 1. Goal

Promote the proven Intel IGCL GPU-frequency capability from a Developer-only Max/Max write probe into the narrow production core required for a future **Minimum GPU Clock** feature.

The product problem is not “force the GPU to maximum clock.”

The target use case is:

~~~text
AAA game under real load
→ Intel iGPU sometimes downclocks farther than desired
→ e.g. B390 may fall toward ~1800–1900 MHz even though its available maximum is higher
→ user wants to prevent unnecessarily deep downclock
→ Intel's ordinary DVFS may still move upward normally
→ package-power / current / thermal protections must remain authoritative
~~~

Therefore the production feature contract is:

~~~text
Addon owns only a minimum GPU frequency floor.

Addon does NOT own:
- maximum GPU frequency;
- GPU voltage;
- GPU PL1/PL2/PL4;
- Intel DVFS above the minimum;
- package TDP policy;
- thermal/current protection.
~~~

PR1 must establish the production primitive and policy without exposing a user setting yet.

---

## 2. Why the product direction changed from the PoC

PR #693 intentionally used:

~~~text
min = hardwareMax
max = hardwareMax
~~~

because that was the smallest unambiguous experiment to prove:

- official IGCL frequency domain discovery;
- `canControl`;
- `ctlFrequencySetRange`;
- immediate `ctlFrequencyGetRange` verification;
- actual B390 hardware behavior.

The 2026-10-06 B390 hardware session proved that primitive.

Representative observed result:

~~~text
RequestedMinMhz = 2300
RequestedMaxMhz = 2300
ReadbackMinMhz  = 2300
ReadbackMaxMhz  = 2300
Verified        = true
NativeResult    = 0x00000000
~~~

The same session also proved why Max/Max is not the desired production policy:

~~~text
RequestMhz      = 2300
ActualMhz       = 2200 / 2250 in constrained samples
TdpMhz          = 2200 / 2250
ThrottleReasons = 0x00000001
~~~

Per Intel's official header:

~~~text
CTL_FREQ_THROTTLE_REASON_FLAG_AVE_PWR_CAP = bit 0
→ average power excursion / PL1
~~~

So an accepted frequency-range request does not override package/power/thermal constraints.

That behavior is desirable.

The production feature should therefore raise only the lower bound while leaving Intel free to:

- boost above the selected minimum when useful;
- reduce actual frequency below the requested minimum when a higher-priority hardware power/thermal/current limit requires it.

Do not describe the production feature as a hard lock or overclock.

---

## 3. Official Intel IGCL facts that govern this PR

Use only the official Intel Graphics Control Library API.

Official source reviewed:

~~~text
https://github.com/intel/drivers.gpu.control-library
include/igcl_api.h
commit b6c462933502e13d1537dd5024949a51be30e63d
~~~

The current upstream `master` resolves to the same commit at the time of this work order.

### 3.1 Runtime binary source

Continue the existing product rule:

~~~text
%SystemRoot%\System32\ControlLib.dll
~~~

Do not package or download Intel's library.

IGCL performance/frequency APIs are 64-bit-only according to Intel's README. The Addon is already x64.

### 3.2 Initialization

Use the same proven Level Zero initialization used by the Developer PoC:

~~~text
ctlInit
Flags = CTL_INIT_FLAG_USE_LEVEL_ZERO
~~~

Do not change the production Intel FPS limiter solely to share this session.

### 3.3 Adapter selection

Reuse the proven selection contract from `IntelGpuIgclProbe`:

~~~text
VendorId == 0x8086
device type == graphics
graphics adapter properties include integrated-adapter flag
~~~

Do not hardcode:

- B390 PCI Device ID;
- Arc 140V PCI Device ID;
- GPU display-name substrings.

The current supported product already identifies exact MSI Claw boards separately:

~~~text
msi.claw.a2vm.7   / MS-1T42
msi.claw.a2vm.8   / MS-1T52
msi.claw.cg3em    / MS-1T91
~~~

GPU-frequency capability is still discovered from IGCL, not inferred from the board name.

This allows the same code path to support:

- Lunar Lake / Intel Arc 140V devices;
- Panther Lake / Intel Arc B390 devices;

without maintaining model-specific MHz tables.

### 3.4 Frequency domain

Enumerate frequency domains and select only:

~~~text
CTL_FREQ_DOMAIN_GPU
~~~

Require readable `ctl_freq_properties_t`.

A production write is available only when:

~~~text
type == GPU
canControl == true
hardware min/max are finite
hardware max > 0
hardware min <= hardware max
~~~

### 3.5 Available clocks

PR1 must add the official API that the Developer PoC does not currently project:

~~~c
ctlFrequencyGetAvailableClocks(
    ctl_freq_handle_t hFrequency,
    uint32_t* pCount,
    double* phFrequency);
~~~

Intel documents that:

- values are non-overclocked hardware clocks;
- values are returned in MHz;
- the returned list is ordered slowest to fastest.

Use the normal two-call pattern:

~~~text
count query
→ allocate exact array
→ value query
~~~

Do not synthesize clock steps.

### 3.6 Range semantics

Intel defines:

~~~text
range.min
→ frequency below which hardware frequency management will not request clocks

input min = 0
→ permit hardware minimum

input min = -1
→ restore factory minimum-frequency limit

output min < 0
→ no external minimum-frequency limit is in effect
~~~

And:

~~~text
range.max
→ frequency above which hardware frequency management will not request clocks

input max = -1
→ restore factory maximum-frequency limit

output max < 0
→ no external maximum-frequency limit is in effect
~~~

The production Addon policy is **minimum-only**.

---

## 4. Locked product policy

### 4.1 Feature identity

The future user-facing feature is:

~~~text
Minimum GPU Clock
~~~

Not:

~~~text
GPU Clock Lock
GPU Overclock
GPU Max Clock
GPU Clock Target
~~~

The product intent is to prevent unnecessarily deep GPU downclock in selected workloads.

### 4.2 Feature default

The feature default is:

~~~text
Off
~~~

PR1 does not persist or expose this setting yet.

### 4.3 Dynamic supported-clock table

Every normal Addon Runtime start must discover the current driver/hardware clock table through:

~~~text
ctlFrequencyGetAvailableClocks()
~~~

The list is Runtime capability state, not persisted product data.

Do not create a disk cache for available clocks.

If the IGCL session must later be genuinely re-created after a real device/driver loss, rediscover the clock table as part of that session initialization.

Do not re-read it on every UI capture or on a timer.

### 4.4 Selectable lower bound

The product's useful-gaming threshold is approximately 1500 MHz.

Define one clear policy constant, for example:

~~~csharp
MinimumUsefulGamingClockMhz = 1500.0;
~~~

This is a **UX/product threshold**, not a statement that Intel hardware minimum is 1500 MHz.

The selectable lower bound is:

~~~text
first actual available clock >= 1500 MHz
~~~

Examples:

~~~text
available contains 1500
→ lower = 1500

available contains 1475, 1525
→ lower = 1525

available contains no value >= 1500 before the computed upper bound
→ production feature unavailable
~~~

Do not round, interpolate, or invent 1500 MHz.

### 4.5 Selectable upper bound

Do not hardcode 2200 MHz.

The selectable upper bound is:

~~~text
highest actual available clock
→ move down exactly two actual supported-clock entries
→ that value is the product upper bound
~~~

Conceptually:

~~~csharp
upper = normalizedAvailableClocks[^3];
~~~

after validating that at least three distinct valid clocks exist.

Example only:

~~~text
... 2200, 2250, 2300
→ upper = 2200
~~~

On another Intel iGPU:

~~~text
... 1925, 1975, 2025
→ upper = 1925
~~~

This policy exists so Lunar Lake 140V and B390 do not need separate hardcoded tables.

### 4.6 Initial future user value

The future first-On/default candidate is:

~~~text
selectable upper bound
= highest available clock minus two supported steps
~~~

PR1 should expose that value in its production capability snapshot/model so PR2 can use it.

PR1 must not turn the feature On automatically.

### 4.7 Selectable values

The selectable list is exactly:

~~~text
actual driver-reported available clocks
intersected with:
  >= dynamic lower bound
  <= dynamic upper bound
~~~

Do not use a continuous MHz slider model in the production core.

The future UI will map a slider index onto this discrete list.

### 4.8 Persisted-target validation contract for follow-up PRs

PR1 should provide one pure validation helper usable by PR2/PR3.

A target is valid only when it matches one of the current selectable clocks.

Allow only tiny floating-point tolerance required for serialization/native representation.

Do not:

- clamp to the nearest supported clock;
- silently move an old saved value upward;
- silently move an old saved value downward.

If a later driver update removes a saved value, the future setting must fail closed until the user selects a valid current value.

---

## 5. Normalize the clock list conservatively

Although Intel documents sorted output, sanitize before product policy is derived.

Recommended sequence:

~~~text
raw available clocks
→ keep finite values > 0
→ order ascending
→ de-duplicate exact/tiny-equivalent duplicates
→ require at least 3 values
→ upper = third from highest
→ lower = first value >= 1500
→ require lower <= upper
→ selectable = values within [lower, upper]
~~~

Do not infer missing intermediate steps.

Do not derive step size and generate a sequence.

The driver-returned entries themselves are authoritative.

---

## 6. Production class shape

Keep the implementation narrow.

Suggested files:

~~~text
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClock.cs
tests/SteamInputAddonforClaw.Tests/IntelGpuMinimumClockTests.cs
~~~

Equivalent naming is acceptable.

Recommended conceptual split:

~~~text
IntelGpuMinimumClockPolicy
  pure available-clock normalization / bounds / target-validation rules

IntelGpuMinimumClockControl
  minimal official IGCL native session
  integrated Intel adapter selection
  GPU frequency-domain selection
  GetAvailableClocks
  GetRange
  SetRange
  optional GetState for diagnostics/logging

IntelGpuMinimumClockRuntime
  one production owner
  capability snapshot
  durable original-min ownership evidence
  ApplyMinimum
  RestoreOriginalMinimum
  future reconcile seam
~~~

Do not introduce:

- GPU manager;
- vendor registry;
- GPU provider abstraction;
- generic tuning framework;
- AMD/NVIDIA interface;
- shared performance-device bus;
- new service/helper process.

One small native-control interface is acceptable only if it materially simplifies deterministic CI tests without Intel hardware.

---

## 7. Do not reuse the Developer probe as the production owner

Current main contains:

~~~text
Diagnostics/IntelGpuIgclProbe.cs
~~~

It is intentionally Developer-only and currently includes:

- Max/Max frequency write testing;
- GPU power-domain enumeration;
- PL1 write/restore experiments;
- Developer-specific operation/status state.

Do not convert that object into the production owner.

Do not make the production feature depend on:

~~~text
FrontendIntelGpuFrequencyProbeSnapshot
FrontendIntelGpuFrequencyProbeOperation
DeveloperPage
~~~

The production core should take only the frequency primitives that are now proven useful.

It is acceptable for the production core to have a small separate frequency-only native projection rather than refactor the Developer probe and Intel FPS limiter into a broad common IGCL framework.

Do not refactor `NativeIgcl` in `IntelFrameLimiter.cs` merely for code reuse.

---

## 8. Minimum-only SetRange behavior

### 8.1 Preconditions

Refuse a production minimum write unless all are true:

~~~text
production capability initialized
integrated Intel GPU selected
GPU frequency domain available
canControl == true
target exactly matches one selectable available clock
Runtime is not shutting down
~~~

### 8.2 Read the current range immediately before write

Before every minimum mutation:

~~~text
ctlFrequencyGetRange()
~~~

This gives the max-side state that must be preserved.

Do not rely on a stale range captured at Runtime startup for the max side.

### 8.3 Preserve max semantics

The Addon does not own the maximum frequency.

Build the SetRange request as:

~~~text
requested.min = selected minimum target

requested.max =
    current.max >= 0
        ? current.max
        : -1
~~~

Meaning:

- an explicit existing external max remains exactly the same;
- an unmanaged/factory max remains factory/unmanaged semantics.

Do not write:

~~~text
max = hardwareMax
max = selectableUpper
max = target
~~~

solely because the Addon is setting a minimum.

### 8.4 Explicit-max conflict

If the current range reports an explicit non-negative max lower than the requested minimum:

~~~text
current.max >= 0
AND current.max < requestedMinimum
~~~

refuse the write.

Do not silently raise another owner's explicit max.

No generalized external-owner arbitration is required.

### 8.5 Native result

For `ctlFrequencySetRange`, ordinary production success is:

~~~text
CTL_RESULT_SUCCESS == 0
~~~

Do not treat `CTL_RESULT_SUCCESS_STILL_OPEN_BY_ANOTHER_CALLER == 1` as SetRange success.

That success-with-information result is relevant to close/shared-caller semantics, not an excuse to accept an unsuccessful range mutation.

### 8.6 Immediate readback is authoritative

After native SetRange success:

~~~text
ctlFrequencyGetRange()
~~~

Production write verification requires:

~~~text
readback minimum == requested minimum within tiny tolerance
AND
max-side semantics remain the same as immediately before the write
~~~

For max:

~~~text
pre-write max >= 0
→ readback max matches that explicit value

pre-write max < 0
→ readback max remains negative / no external max
~~~

If the minimum was accepted but max semantics changed, the operation is not verified.

Do not call it successful merely because `actual` clock rose.

### 8.7 Actual frequency is observational

If `ctlFrequencyGetState` is sampled for logs:

~~~text
request
actual
tdp
throttleReasons
~~~

are evidence only.

Do not require:

~~~text
actual >= selected minimum
~~~

for SetRange verification.

Intel power/thermal/current limits remain higher-priority hardware constraints.

---

## 9. Durable minimum-frequency ownership

A production setting may survive longer than one process lifetime, so Developer-PoC in-memory restore is not enough.

Add one narrow ownership file, for example:

~~~text
intel-gpu-minimum-clock-ownership.json
~~~

under the existing Addon data root.

Add the corresponding path through `AddonDataPaths`.

### 9.1 What the marker represents

The marker means:

> The Addon has attempted to establish an external GPU minimum-frequency limit and still owes a restore to the pre-ownership minimum semantics when production policy releases ownership.

Persist only the information needed for that responsibility.

Recommended content:

~~~json
{
  "vendorId": 32902,
  "deviceId": 45184,
  "originalMinMhz": -1
}
~~~

An explicit original minimum is stored as its exact value.

A negative original minimum preserves the official “no external minimum limit” semantics.

Do not persist the available clock list.

Do not turn the marker into a second Device/Profile settings store.

### 9.2 Persist recovery evidence before the first write

When there is no existing owned state:

~~~text
GetRange
→ capture original min semantics
→ persist ownership marker
→ only after marker persistence succeeds:
   attempt SetRange
~~~

If marker persistence fails:

~~~text
do not call SetRange
~~~

This is the critical crash-safety ordering.

### 9.3 Retain ownership evidence after native write success

Once SetRange returns success:

- keep the marker even if immediate readback fails;
- keep ownership state even if later diagnostic/state capture fails.

A successful write may have changed hardware.

Only verified release may clear the marker.

### 9.4 Native write failure after marker persistence

If SetRange itself returns failure before any verified production mutation:

- it is acceptable to retain the marker conservatively;
- a later release/recovery may restore the captured original minimum;
- do not add complex “did the driver partially mutate?” inference.

One extra harmless restore is preferable to losing the only recovery evidence.

---

## 10. Restore only the Addon-owned minimum

When production policy later releases minimum-clock ownership:

~~~text
load original minimum from marker
→ read CURRENT range
→ preserve the CURRENT max semantics
→ restore original minimum
→ immediate GetRange readback
~~~

Build the request as:

~~~text
restore.min =
    original min >= 0
        ? original min
        : -1

restore.max =
    current max >= 0
        ? current max
        : -1
~~~

This intentionally does not restore an old captured max.

The Addon does not own maximum frequency.

Verification:

~~~text
original min >= 0
→ readback min matches original

original min < 0
→ readback min is negative

max-side semantics
→ remain the same as immediately before restore
~~~

Only then:

~~~text
delete ownership marker
clear in-memory ownership state
~~~

If restore fails:

~~~text
keep marker
report failure
allow later retry
~~~

---

## 11. Startup / restart / crash contract

PR1 establishes the reusable production semantics, but does not yet have a persisted Device target to apply.

The core must support the following PR2 orchestration without redesign.

### 11.1 No marker

~~~text
future effective setting Off
→ do nothing

future effective setting On
→ capture original min
→ persist marker
→ apply selected min
~~~

### 11.2 Marker exists after controlled Runtime replacement

A controlled Runtime restart must not be interpreted as “user disabled Minimum GPU Clock.”

Future PR2 behavior:

~~~text
marker exists
+ persisted feature still On
→ adopt existing ownership evidence
→ re-apply current effective target if necessary
→ keep original-min baseline from marker
~~~

Do not overwrite the original baseline merely because the process restarted.

### 11.3 Marker exists after crash

Same rule.

Crash recovery is not a separate authority model.

~~~text
marker exists
+ setting On
→ reuse marker baseline
→ reconcile desired minimum

marker exists
+ setting Off / target unavailable
→ restore original minimum
→ verify
→ clear marker
~~~

PR1 should make these operations deterministic and testable.

### 11.4 PR1 startup behavior

Because PR1 has no persisted user setting yet, normal mainline behavior after this PR is read-only:

~~~text
ReconcileDeviceProfileStartup
→ initialize IntelGpuMinimumClockRuntime
→ discover integrated Intel GPU
→ read properties
→ read available clocks
→ derive production selectable policy
→ log capability once
→ DO NOT write a frequency range
~~~

This satisfies the requirement that current available clocks are read fresh on each Addon Runtime start.

Do not create a background polling loop.

---

## 12. Sleep / Hibernate / Resume readiness

Sleep/Hibernate/Resume is a real supported lifecycle and the production core must not assume its IGCL handles can never become stale.

However PR1 must not add a new power state machine.

Required core behavior:

- keep one long-lived session during normal Runtime lifetime;
- expose a bounded session-reset/reinitialize primitive for a real IGCL device-loss/unavailable result;
- session reinitialization must rediscover:
  - integrated Intel adapter;
  - GPU frequency domain;
  - properties;
  - available clocks;
  - dynamic selectable policy.

Future PR2 resume flow will be:

~~~text
existing Addon PowerResumeObserved
→ recompute effective Device target
→ reconcile minimum

normal valid IGCL session
→ use it directly

real DEVICE_LOST / DEVICE_UNAVAILABLE from IGCL
→ one bounded session reinitialize
→ rediscover available clocks
→ retry reconcile once
~~~

Do not add:

- periodic health polling;
- resume epoch;
- watchdog;
- retry manager;
- arbitrary retry loops.

PR1 tests should cover the session-reset primitive without wiring a new host-level resume owner.

---

## 13. Runtime shutdown

Production minimum-clock ownership is a persistent user setting, not a Developer experiment.

Therefore the production core must **not automatically restore the original minimum merely because the Runtime performs a normal controlled restart**.

Future desired behavior:

~~~text
controlled Runtime restart
→ close IGCL handles
→ keep durable marker
→ replacement Runtime adopts marker
→ replacement Runtime reconciles desired target
~~~

PR1 may only close its native resources on normal shutdown.

The existing Developer probe remains different:

~~~text
Developer probe mutation
→ best-effort restore on process shutdown
~~~

Do not merge those lifecycle policies.

---

## 14. Current Developer probe coexistence

PR1 leaves the current Developer-only probe intact.

Do not:

- remove the Developer card;
- remove PL1 diagnostics;
- rewrite its frontend protocol;
- make the production core depend on it.

Also do not expose production minimum-clock mutation yet, so there is no normal-user concurrent production/probe ownership in PR1.

Before PR2 exposes live Device Minimum GPU Clock, that follow-up must explicitly decide the Developer frequency write surface:

- retire/disable its conflicting frequency mutation while production ownership is active; or
- otherwise ensure it cannot overwrite the production floor behind the production owner's back.

Do not solve this in PR1 with a generic multi-owner arbitration layer.

---

## 15. GPU power-limit policy is explicitly excluded

The 2026-10-06 B390 hardware result showed:

~~~text
IGCL GPU frequency domain
→ available / controllable

IGCL GPU power domain
→ unavailable
→ "IGCL returned no power domain with readable properties and limits."
~~~

Meanwhile ClawHUD / PresentMon telemetry showed meaningful GPU power readings in real games, including approximately 20 W under load.

Those are separate facts:

~~~text
PresentMon GPU power telemetry
→ useful observation

IGCL power-domain mutation
→ not available on the tested B390 path
~~~

PR1 must not add production GPU PL1 control.

Do not couple Minimum GPU Clock to ClawHUD GPU-power telemetry.

Addon remains the control owner; ClawHUD remains an observation surface.

---

## 16. Full1902 independence

Minimum GPU Clock is an independent performance capability.

It must not depend on:

- Center M Enabled vs Disabled controller authority;
- PID1901/PID1902;
- HidHide;
- VIIPER;
- Steam Deck/X360 presentation;
- WING suppression;
- controller physical ownership.

Conceptually:

~~~text
supported MSI Claw
+ integrated Intel GPU
+ official IGCL frequency capability
→ Minimum GPU Clock capability may exist
~~~

Do not put this feature inside controller-routing composition.

Follow the existing Device/Profile performance sibling pattern:

~~~text
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
IntelFrameLimiterRuntime
IntelGpuMinimumClockRuntime
~~~

without creating a shared “performance authority manager.”

---

## 17. Logging

Use a production category distinct from the Developer diagnostic, for example:

~~~text
Profiles.IntelGpuMinimumClock
~~~

### 17.1 Startup capability

One INFO/DEBUG capability record per Runtime initialization is enough.

Include:

~~~text
AdapterName
VendorId
DeviceId
CanControl
HardwareMinMhz
HardwareMaxMhz
AvailableClockCount
SelectableClockCount
SelectableMinMhz
SelectableMaxMhz
RecommendedDefaultMhz
OwnershipMarkerPresent
~~~

Do not dump one log line per available clock unless Developer/Debug logging explicitly needs a single compact list.

### 17.2 Mutation

On future ApplyMinimum / RestoreOriginalMinimum operations log:

~~~text
Reason
RequestedMinMhz
PreWriteMinMhz
PreWriteMaxMhz
ReadbackMinMhz
ReadbackMaxMhz
Verified
NativeResult
OwnershipMarkerPresent
~~~

If state capture is available, it may additionally log:

~~~text
RequestMhz
ActualMhz
TdpMhz
ThrottleReasons
~~~

Do not classify lower ActualMHz alone as mutation failure.

No per-frame/per-second polling logs.

---

## 18. Native ABI

Project only the frequency structures/functions needed by production:

~~~text
ctl_init_args_t
ctl_application_id_t
ctl_device_adapter_properties_t
ctl_freq_domain_t
ctl_freq_properties_t
ctl_freq_range_t
ctl_freq_state_t                  // optional but useful for logging
ctlInit
ctlClose
ctlEnumerateDevices
ctlGetDeviceProperties
ctlEnumFrequencyDomains
ctlFrequencyGetProperties
ctlFrequencyGetAvailableClocks
ctlFrequencyGetRange
ctlFrequencySetRange
ctlFrequencyGetState              // optional observational evidence
~~~

Keep `CallingConvention.Cdecl`.

Retain exact x64 layout tests.

Known header-derived frequency structure sizes already proven by the PoC:

~~~text
ctl_freq_properties_t = 32 bytes
ctl_freq_range_t      = 24 bytes
ctl_freq_state_t      = 56 bytes
~~~

Do not project power-limit structs into the production core.

---

## 19. Error policy

Production frequency control must fail closed.

### Initialization/capability failure

~~~text
ControlLib missing
ctlInit failure
no integrated Intel GPU
no GPU frequency domain
GetProperties failure
canControl == false
GetAvailableClocks failure
invalid/insufficient selectable table
→ capability unavailable
→ no frequency write
~~~

### Apply failure

~~~text
invalid target
explicit max conflict
marker persistence failure
SetRange failure
GetRange readback failure
readback mismatch
max semantics changed unexpectedly
→ operation fails
→ retain recovery evidence where a write may have happened
~~~

Do not terminate the whole Addon Runtime because optional GPU minimum-frequency control is unavailable.

Controller/HidHide/VIIPER safety is unrelated and must continue normally.

---

## 20. Tests

No CI test may perform real GPU writes.

### 20.1 Available-clock policy

Add pure tests for:

~~~text
sorted normal table
unsorted input is normalized
duplicate entries
non-finite / <= 0 entries removed
exact 1500 exists
1500 missing → first supported value above 1500
highest minus two supported entries defines upper
no hardcoded 2200 assumption
different synthetic B390-like and 140V-like tables produce different valid bounds
fewer than three clocks → unavailable
lower > computed upper → unavailable
selectable list contains only driver-returned values
recommended default == selectable upper
~~~

Do not encode guessed real Arc 140V MHz values as product truth.

### 20.2 Target validation

~~~text
exact selectable target → valid
tiny serialization tolerance → canonical driver value accepted
unsupported middle value → invalid
below dynamic lower → invalid
above dynamic upper → invalid
removed saved value → invalid, no nearest-neighbor correction
~~~

### 20.3 Min-only request policy

Test:

~~~text
current max explicit
→ request preserves exact max

current max negative
→ request uses -1 factory/unmanaged semantics

explicit current max < requested min
→ refuse write
~~~

### 20.4 Readback verification

Test:

~~~text
minimum matches + explicit max preserved
→ verified

minimum mismatch
→ not verified

pre-write max negative + readback max negative
→ verified max semantics

pre-write max explicit + readback max changed
→ not verified
~~~

### 20.5 Ownership marker

Use a temporary directory.

Test:

~~~text
marker written before first SetRange call
marker persistence failure → SetRange not called
successful Set + readback → marker retained
Set success + readback failure → marker retained
restore verified → marker deleted
restore failed → marker retained
restart/new runtime instance reads original-min marker without overwriting it
~~~

Do not add a database or journaling framework.

### 20.6 Native ABI

Keep/extend exact size/offset tests for the production frequency projection.

Add delegate/struct coverage for `ctlFrequencyGetAvailableClocks`.

### 20.7 Runtime startup read-only behavior

Test that PR1 host startup:

~~~text
initializes/discovers capability
does not call SetRange
does not create ownership marker
does not modify Device/Profile persistence
~~~

---

## 21. Host wiring in PR1

Keep host changes minimal.

### Construction

Add one production core instance alongside the existing performance runtimes.

Do not place it in routing/controller composition.

### Startup

Inside the existing delayed Device/Profile startup phase:

~~~text
ReconcileDeviceProfileStartup()
~~~

call production minimum-clock initialization/read-only capability discovery.

This work must not delay or block initial Full1902 controller ownership/routing startup.

The existing comment/contract that Device/Profile work occurs after controller Runtime initialization remains authoritative.

### Shutdown

Close the production IGCL session with the other performance runtimes.

Do not restore an owned production minimum solely due normal shutdown.

PR1 normally owns no minimum yet, so no user-visible frequency mutation should occur.

### No frontend protocol change

PR1 adds no user-facing frontend API.

Therefore:

~~~text
FrontendTransportProtocol
OverlayTransportProtocol
~~~

must not be bumped merely for this core.

---

## 22. Expected source changes

Likely:

~~~text
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClock.cs
src/SteamInputAddonforClaw/Install/AddonDataPaths.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

tests/SteamInputAddonforClaw.Tests/IntelGpuMinimumClockTests.cs
tests/SteamInputAddonforClaw.Tests/AddonProcessHost*   // only if existing host tests need focused startup assertions
~~~

The exact split may follow current repository conventions.

Avoid unrelated changes to:

~~~text
DeviceSettings.cs
GameProfile.cs
QuickSettings
Overlay
DeveloperPage
IntelFrameLimiter.cs
IntelGpuIgclProbe.cs
Frontend transport
controller ownership
HidHide
VIIPER
TDP helper
~~~

unless a tiny compile/test-only adjustment is strictly required.

---

## 23. Hardware validation after PR1

PR1 does not expose a production write UI.

Hardware acceptance is therefore primarily capability/discovery validation plus regression safety.

### B390 / CG3EM

On MSI Claw 8 EX AI+ / CG3EM:

Verify startup log reports:

~~~text
integrated Intel adapter selected
GPU frequency domain available
canControl = true
hardware min/max sane
available clocks discovered
selectable lower = first actual clock >= 1500
selectable upper = actual maximum minus two supported entries
recommended default = selectable upper
no production SetRange issued
~~~

Record the actual returned available-clock list once in Debug evidence if practical.

Do not turn that observed list into hardcoded policy.

### Lunar Lake / Arc 140V / A2VM

On MSI Claw 7/8 AI+ A2VM:

Verify the exact same code path:

~~~text
integrated Intel adapter selected
GPU domain discovered
available clock table read from that machine
dynamic bounds derived from that table
no B390 MHz constants involved
no production SetRange issued
~~~

Physical 140V validation is required before claiming the feature fully validated on A2VM, but the code must not require a separate 140V branch.

### Regression

Confirm:

- controller Full1902 startup unchanged;
- Intel FPS limiter still works independently;
- Developer IGCL probe still opens and operates as before;
- no new WARN/ERROR loop when IGCL frequency capability is absent;
- normal Addon shutdown remains clean.

---

## 24. Explicit non-goals

PR1 must not add:

~~~text
Device Minimum GPU Clock persistence
Game Profile Minimum GPU Clock persistence
AC/DC user values
Steam active-game override
XBOX active-game override
Device page UI
Steam Profile UI
XBOX Profile UI
Quick Settings row
Overlay row
frontend mutation RPCs
frontend protocol bump
overlay protocol bump
automatic TDP adjustment
GPU power-target control
GPU PL1 / PL2 / PL4 production mutation
ClawHUD dependency
PresentMon dependency for control
hardcoded B390 2200 MHz default
hardcoded Arc 140V clock table
continuous MHz slider synthesis
maximum-frequency ownership
voltage control
overclocking
periodic GPU clock polling
periodic reassert loop
generic GPU manager/provider abstraction
AMD/NVIDIA support layer
new helper process
new service
new watchdog
new lifecycle epoch/state machine
controller/HidHide/VIIPER changes
~~~

---

## 25. Follow-up PR boundaries

This work is the first of three production PRs.

### PR1 — this work order

~~~text
production official-IGCL frequency core
dynamic available-clock policy
minimum-only mutation primitive
ownership/restore evidence
startup capability discovery
tests
~~~

### PR2 — Device Minimum GPU Clock

Will add:

~~~text
DevicePerformanceSettings.GpuClock
Enabled
AC value
DC value
default Off
first-On default = PR1 RecommendedDefaultMhz
Device page discrete slider
startup / AC-DC / resume reconcile wiring
production ownership activation/release
~~~

At that point the Developer frequency mutation surface must be reviewed for conflict with active production ownership.

### PR3 — Steam/XBOX per-game override

Will add:

~~~text
GamePerformanceOverrides.GpuClock
XboxGamePerformanceOverrides equivalent/shared field as current profile model permits
Steam profile UI
XBOX profile UI
ActiveProfileResolver priority
active-game transition reconcile
Quick Settings / Overlay projection as appropriate
~~~

Do not pull PR2/PR3 persistence or UI concerns into PR1.

---

## 26. Acceptance criteria

PR1 is complete when all are true:

1. Production code uses only official System32 Intel IGCL for frequency control.
2. The production core selects an integrated Intel graphics adapter by capability, not GPU name.
3. `CTL_FREQ_DOMAIN_GPU` is selected and `canControl` is honored.
4. `ctlFrequencyGetAvailableClocks` is projected and called on normal Runtime startup.
5. No available-clock table is persisted.
6. The selectable lower bound is the first actual supported clock at or above 1500 MHz.
7. The selectable upper bound is the actual highest clock minus two actual supported entries.
8. No 2200 MHz product constant exists.
9. The recommended future default equals that dynamic upper bound.
10. Only actual returned clock entries can become valid targets.
11. Production SetRange changes only minimum-frequency policy while preserving current max semantics.
12. An explicit current max lower than the requested min is never overwritten.
13. Immediate GetRange verifies both requested min and preserved max semantics.
14. Lower actual frequency caused by PL1/thermal/current constraints is not classified as SetRange failure.
15. Durable recovery evidence is persisted before the first production SetRange attempt.
16. Verified restore removes only the Addon's minimum-frequency ownership and preserves current max semantics.
17. Failed/unverified restore retains recovery evidence.
18. PR1 normal startup performs discovery only and writes no production GPU frequency.
19. No Device/Profile schema or frontend protocol changes are included.
20. No new manager/service/watchdog/race-defense architecture is introduced.
21. Existing Full1902 controller lifecycle remains unchanged.
22. Existing Developer IGCL probe and Intel FPS limiter remain independent and functional.
23. CI tests cover policy, min-only request construction, readback verification, ownership marker lifecycle, and x64 ABI without hardware writes.
24. B390 startup discovery is hardware-validated.
25. Lunar Lake/Arc 140V uses the same capability-driven implementation and is ready for physical A2VM validation.

---

## 27. Implementation principle

Keep the production contract narrow:

~~~text
Intel driver says which clocks exist.
Addon chooses a safe gaming-oriented subset from those exact clocks.
User will later choose a minimum floor.
Addon owns only that minimum floor.
Intel still owns boost, maximum clock, power, thermals, current limits, and actual resolved clock.
~~~

That is the entire feature boundary.

Do not expand it into a GPU tuning framework.
