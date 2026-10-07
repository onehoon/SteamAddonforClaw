# Work Order — PR6: Production Minimum GPU Clock File Organization

> **Date:** 2026-10-07  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `main@bc284d25be711006eef5142bf6605c35dc1ea5f6` (PR #707 merged)  
> **Product architecture:** standalone Full1902  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and the active Full1902 architecture chain  
> **Current Frontend protocol:** `FrontendTransportProtocol.CurrentVersion = 65`  
> **Overlay protocol:** unchanged by this PR  
> **Scope:** physically separate the stabilized production Minimum GPU Clock implementation into Policy / Runtime / Control source files without changing behavior, contracts, lifecycle, ownership, logging semantics, native ABI, or tests  
> **Out of scope:** new abstractions/interfaces, product-policy changes, UI changes, frontend/RPC changes, Intel FPS refactoring, IGCL PL1 work, controller architecture, CTW integration

---

## 1. Goal

Organize the finalized production Minimum GPU Clock implementation by its existing responsibility boundaries.

PR #706 established the final product policy:

~~~text
active enabled Steam/XBOX game
+ that game's Minimum GPU Clock enabled
    → apply current AC/DC game minimum-frequency floor

otherwise
    → min = -1
    → Intel factory minimum-frequency policy
~~~

PR #707 removed the completed Developer IGCL frequency / PL1 PoC and the compatibility guards that existed only because that PoC was a second frequency writer.

The remaining production implementation is now stable enough to split physically without redesigning it.

Current source:

~~~text
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClock.cs
~~~

Current reviewed size:

~~~text
1,073 lines
~~~

The file already contains three clear responsibility groups:

~~~text
Policy / shared value types
Runtime / lifecycle + active-game reconciliation
Control / native IGCL session + ABI
~~~

This PR should make those existing boundaries visible in the file layout.

---

## 2. This is a no-behavior-change PR

The most important constraint is:

> **Move existing complete types to responsibility-specific files; do not redesign their behavior.**

Do not use the file split as an opportunity to:

- rename public/internal product concepts;
- change method signatures;
- change return types;
- change exception behavior;
- change locks;
- change lifecycle ordering;
- change retries;
- change log levels or messages;
- change target validation;
- change selectable clocks;
- change factory release;
- change startup/resume/uninstall;
- change Steam/XBOX profile behavior;
- change native IGCL loading;
- change native ABI layouts;
- change the existing `IIntelGpuMinimumClockControl` seam;
- extract helpers into new managers/providers/services.

If a line does not need to change to move a type into the correct file, leave it unchanged.

---

## 3. Current reviewed structure

The current `IntelGpuMinimumClock.cs` contains these top-level types in this order:

~~~text
IntelGpuFrequencyRange
IntelGpuMinimumClockNativeCapability
IntelGpuMinimumClockCapability
IntelGpuMinimumClockOperationResult
IIntelGpuMinimumClockControl

IntelGpuMinimumClockPolicy
    ClockSelection

IntelGpuMinimumClockRuntime

IntelGpuMinimumClockControl
    native IGCL delegates
    native IGCL structs

IgclMinimumClockException
UnavailableIntelGpuMinimumClockControl
~~~

Approximate responsibility ranges on reviewed main:

~~~text
Policy / shared definitions
    lines 9-198

Runtime
    lines 199-710

Control / native IGCL
    lines 711-1073
~~~

The code is already separated conceptually. This PR should not add another architecture layer.

---

## 4. Target file layout

Replace the monolithic source with exactly these three production files:

~~~text
src/SteamInputAddonforClaw/Profiles/Performance/
    IntelGpuMinimumClockPolicy.cs
    IntelGpuMinimumClockRuntime.cs
    IntelGpuMinimumClockControl.cs
~~~

Delete:

~~~text
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClock.cs
~~~

Do not retain an empty compatibility file.

The SDK-style runtime project uses default `Compile` inclusion, so no `.csproj` source-file list change should be necessary.

---

## 5. IntelGpuMinimumClockPolicy.cs

Move the pure policy/value types here.

Target contents:

~~~text
IntelGpuFrequencyRange

IntelGpuMinimumClockCapability

IntelGpuMinimumClockPolicy
    MinimumUsefulGamingClockMhz
    FrequencyToleranceMhz

    SelectAvailableClocks(...)
    IsHardwareRangeValid(...)
    TryGetCanonicalTarget(...)
    TryCreateMinimumRequest(...)
    TryCreateFactoryMinimumReleaseRequest(...)
    MatchesReadback(...)
    MatchesRangeSide(...)

    ClockSelection
~~~

### 5.1 Preserve policy exactly

Do not change:

~~~text
MinimumUsefulGamingClockMhz = 1500.0
FrequencyToleranceMhz = 0.1

available-clock normalization
minimum of three distinct valid clocks
dynamic upper bound = third-from-highest clock
lower bound = first available clock >= 1500 MHz
recommended default = top selectable clock
canonical target tolerance
explicit max preservation
factory release min = -1
negative max semantics
readback matching semantics
~~~

Do not replace this with LINQ-heavy rewrites, generic range helpers, or reusable GPU abstractions.

### 5.2 Shared value type placement

Keep `IntelGpuFrequencyRange` in the policy file because it is the shared semantic range used by both Runtime and Control.

Keep `IntelGpuMinimumClockCapability` here because it is the production-normalized capability consumed by Runtime/frontend projection, not the raw native discovery result.

No new `Models.cs`, `Contracts.cs`, or common GPU file is needed.

---

## 6. IntelGpuMinimumClockRuntime.cs

Move the production lifecycle/effective-policy owner here.

Target contents:

~~~text
IntelGpuMinimumClockOperationResult

IntelGpuMinimumClockRuntime
~~~

Keep all current Runtime members and behavior, including:

~~~text
DeviceLostResult
DeviceUnavailableResult

SetActiveProfileResolver(...)

ResolveSelectableClockIndex(...)
ResolveSelectableClockOrRecommendedDefault(...)

InitializeReadOnly()
StartupReconcile()
CaptureCapability()

ReconcileEffective(...)
ReconcileAfterResume()
ReconcileGameRailMutation(...)

PrepareForUninstall()
TryReinitializeSession(...)

ApplyMinimum(...)
ReleaseToFactoryMinimum(...)

BeginShutdown()
Dispose()

LoadProfile()
ReconcileLoadedEffective(...)
NoOpResult()
FailedOperation()

LogEffectiveReconcile(...)
CanonicalOrDefault(...)
BuildCapability(...)
UnavailableCapability(...)
LogCapability(...)
CompleteAndLog(...)
~~~

### 6.1 Runtime remains the product authority

Do not move active-game policy into Control.

The Runtime remains responsible for:

~~~text
ProfileStore read
ProfileMutationGate synchronization
active Steam/XBOX profile resolution
whole-profile Enabled gate
GameGpuMinimumClockSettings Enabled gate
AC/DC rail selection
unsupported-target fail-close
power-source-read fail-close
startup reconcile
resume reconcile
one bounded IGCL session reinitialize
uninstall factory release
operation logging
~~~

### 6.2 Keep current locking

Keep the current Runtime `_gate`.

Do not introduce:

- a second runtime lock;
- reader/writer locks;
- epochs;
- async gates;
- a reconcile queue;
- a state machine;
- a GPU ownership manager.

The current supported single-user/single-interactive-session product lifecycle does not justify additional synchronization for this file split.

### 6.3 Keep bounded recovery exactly as-is

The only native-result-triggered reinitialize path remains:

~~~text
CTL_RESULT_ERROR_DEVICE_LOST     = 0x40000003
CTL_RESULT_ERROR_DEVICE_UNAVAILABLE = 0x40000027
~~~

The existing behavior remains:

~~~text
normal reconcile failure
+ allowSessionReinitialize
+ native result is DEVICE_LOST / DEVICE_UNAVAILABLE
    → Reinitialize once
    → Reconcile once

otherwise
    → no generic retry
~~~

Do not broaden recovery.

---

## 7. IntelGpuMinimumClockControl.cs

Move the hardware/native boundary here.

Target contents:

~~~text
IntelGpuMinimumClockNativeCapability

IIntelGpuMinimumClockControl

IntelGpuMinimumClockControl

IgclMinimumClockException

UnavailableIntelGpuMinimumClockControl
~~~

Keep all private native declarations inside `IntelGpuMinimumClockControl` exactly as today:

~~~text
ApplicationId
InitArgs
AdapterProperties
FirmwareVersion
AdapterBdf
FrequencyProperties
FrequencyRange
FrequencyState

CtlInit
CtlClose
CtlEnumerateDevices
CtlGetDeviceProperties
CtlEnumerateDomains
CtlGetFrequencyProperties
CtlGetAvailableClocks
CtlGetRange
CtlSetRange
~~~

### 7.1 Keep native loading exactly as-is

Preserve:

~~~text
NativeLibrary.Load(
    Path.Combine(Environment.SystemDirectory, "ControlLib.dll"))
~~~

Do not:

- bundle ControlLib.dll;
- search DriverStore;
- add alternate Intel library paths;
- add private-provider fallback;
- share IntelFrameLimiter's native session;
- extract a common IGCL loader.

The existing production Minimum GPU Clock control owns its own bounded native session.

### 7.2 Keep adapter/domain discovery exactly as-is

Preserve:

~~~text
VendorId == 0x8086
DeviceType == graphics
integrated-adapter flag required
CTL_FREQ_DOMAIN_GPU only
~~~

Do not add device-model or PCI-ID tables.

### 7.3 Keep available-clock discovery exactly as-is

Preserve the current two-call:

~~~text
ctlFrequencyGetAvailableClocks(count)
ctlFrequencyGetAvailableClocks(values)
~~~

including current returned-count validation.

### 7.4 Keep minimum-only range writes exactly as-is

Control remains mechanically responsible only for:

~~~text
GetRange()
SetRange(range)
~~~

Policy/Runtime decide what the range means.

Do not add max-frequency product policy to Control.

### 7.5 Keep ABI test surface

Keep:

~~~text
IntelGpuMinimumClockControl.NativeAbiIsExpectedForTests()
~~~

and all currently validated struct sizes/offsets and delegate calling convention checks.

Even if a native struct appears lightly used, do not remove ABI declarations as incidental cleanup in this PR.

---

## 8. Shared-type ownership map

Use this exact conceptual ownership unless a compile requirement forces a trivial adjustment:

| Type | Target file | Reason |
|---|---|---|
| `IntelGpuFrequencyRange` | Policy | shared semantic range |
| `IntelGpuMinimumClockCapability` | Policy | normalized product capability |
| `IntelGpuMinimumClockNativeCapability` | Control | raw native discovery result |
| `IntelGpuMinimumClockOperationResult` | Runtime | runtime operation/reconcile result |
| `IIntelGpuMinimumClockControl` | Control | hardware boundary/test seam |
| `IntelGpuMinimumClockPolicy` | Policy | pure calculations/validation |
| `IntelGpuMinimumClockRuntime` | Runtime | product/lifecycle authority |
| `IntelGpuMinimumClockControl` | Control | native IGCL implementation |
| `IgclMinimumClockException` | Control | native operation failure |
| `UnavailableIntelGpuMinimumClockControl` | Control | existing unavailable hardware implementation |

Do not introduce a fourth file solely for shared records.

---

## 9. Preserve existing type/member names

Keep all current type names and member signatures.

In particular, do not rename:

~~~text
IIntelGpuMinimumClockControl
IntelGpuMinimumClockNativeCapability
IntelGpuMinimumClockCapability
IntelGpuMinimumClockOperationResult
IntelGpuFrequencyRange
IgclMinimumClockException
UnavailableIntelGpuMinimumClockControl
~~~

This PR is not an API naming cleanup.

The current internal test seam is useful and already paid for. Do not replace it with another interface.

---

## 10. Preserve existing cross-type constants

Current `IntelGpuMinimumClockControl` references:

~~~text
IntelGpuMinimumClockRuntime.DeviceLostResult
IntelGpuMinimumClockRuntime.DeviceUnavailableResult
~~~

Do not introduce a new shared native-result constants class merely to avoid that cross-file reference.

Do not duplicate the magic values into multiple files.

Preserve the existing single authority for these two constants unless the compiler forces a trivial visibility correction.

A future change may reconsider type ownership if there is a concrete need; this PR should not.

---

## 11. Required usings after split

Keep each file's dependencies narrow.

Expected approximate ownership:

### Policy

No native interop dependencies.

Do not import:

~~~text
System.Reflection
System.Runtime.InteropServices
System.Text
SteamInputAddonforClaw.Diagnostics
~~~

unless unexpectedly required by actual moved code.

### Runtime

Expected to need product/profile/logging dependencies such as:

~~~text
SteamInputAddonforClaw.Diagnostics
SteamInputAddonforClaw.Profiles
~~~

Do not import native interop namespaces.

### Control

Expected to own:

~~~text
System.Reflection
System.Runtime.InteropServices
System.Text
SteamInputAddonforClaw.Diagnostics
~~~

Do not import ProfileStore / active-profile policy dependencies.

This is source hygiene only. Do not create wrapper types to enforce it.

---

## 12. Call sites should remain unchanged

The physical file split must not require behavioral changes in:

~~~text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
~~~

Current host composition remains conceptually:

~~~text
IntelGpuMinimumClockControl
    ↓
IntelGpuMinimumClockRuntime
    ↓
existing startup / active-game / AC-DC / resume / uninstall lifecycle
~~~

The frontend continues using the same `IntelGpuMinimumClockRuntime` methods.

If these files need edits beyond formatting/import fallout, stop and verify that the split is not accidentally turning into a behavior refactor.

---

## 13. Frontend and protocol are untouched

Do not change:

~~~text
SteamInputAddonforClaw.Contracts
SteamInputAddonforClaw.FrontendTransport
FrontendTransportProtocol.CurrentVersion = 65
OverlayTransportProtocol
~~~

There is no wire-contract change.

Do not bump protocol versions.

Do not modify Steam/XBOX GPU Minimum Clock RPCs.

---

## 14. UI and persistence are untouched

Do not change:

~~~text
ProfilePage
XboxPage
GpuMinimumClockUiPolicy
Profile Quick Settings
GameProfile persistence
ProfileStore schema
~~~

The game-only UX and persistence established by PR #706 remain exactly as-is.

No Device/global GPU control may be reintroduced.

---

## 15. Production behavior that must remain identical

The resulting three-file implementation must still satisfy all of these existing contracts.

### Startup

~~~text
IGCL capability discovery
→ active enabled game GPU Min On
    → apply current game rail
→ otherwise
    → min = -1
~~~

### Game start

~~~text
game profile GPU Min On
→ apply current AC/DC target
~~~

### Game exit

~~~text
no active owning game
→ min = -1
→ readback verify
~~~

### Game A → Game B

~~~text
A On → B On
→ directly apply B target

A On → B Off/null
→ min = -1
~~~

### Whole profile Off

~~~text
→ min = -1
~~~

### AC/DC change

~~~text
active owning game
→ apply matching game rail

no owning game
→ factory minimum
~~~

### Unsupported saved target

~~~text
do not clamp
do not choose nearest
→ min = -1
→ report failure
~~~

### Unknown power source

~~~text
do not guess
do not keep stale game rail
→ min = -1
~~~

### Resume

~~~text
existing 2.5 s host settle
→ ReconcileAfterResume()
→ bounded DEVICE_LOST / DEVICE_UNAVAILABLE recovery only
~~~

### Uninstall

~~~text
PrepareForUninstall()
→ always establish min = -1
→ readback min < 0
→ failure blocks uninstall
~~~

### Maximum frequency

~~~text
current explicit max
→ preserve it

current factory/no-external max
→ preserve factory semantics with -1
~~~

No behavior above may change in this PR.

---

## 16. Logging must remain equivalent

Keep category:

~~~text
Profiles.IntelGpuMinimumClock
~~~

Keep existing capability/reconcile/operation logging fields and severity.

Do not rename logs merely because source files changed.

Important existing evidence remains:

~~~text
Reason
EffectiveSource
PowerSource
RequestedMinMhz
PreWriteMinMhz
PreWriteMaxMhz
ReadbackMinMhz
ReadbackMaxMhz
Verified
NativeResult
NativeSetResult
NativeReadResult
Failure
~~~

Source-file organization should not affect field diagnostics.

---

## 17. Tests

The existing production GPU test suite already targets the types rather than the source filename.

Primary existing coverage:

~~~text
tests/SteamInputAddonforClaw.Tests/IntelGpuMinimumClockTests.cs
tests/SteamInputAddonforClaw.Tests/IntelGpuMinimumClockFrontendTests.cs
tests/SteamInputAddonforClaw.Tests/XboxGameProfileFrontendTests.cs
~~~

Do not split these test files merely to mirror production files.

Do not rewrite tests for style.

### 17.1 Expected test changes

Ideally:

~~~text
zero behavioral test changes
~~~

Only update a test if compilation proves it has a source-file-specific assumption.

Repository review found no live source/test reference to the literal production path:

~~~text
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClock.cs
~~~

Historical work-order documents do reference the old path; leave those historical documents unchanged.

### 17.2 Required production coverage to continue passing

At minimum retain existing coverage for:

- selectable clock normalization;
- dynamic upper/lower bounds;
- recommended default;
- canonical target validation;
- minimum request preserving max;
- factory release request using `min = -1`;
- readback verification;
- x64 IGCL ABI layout;
- game-only Steam apply/release;
- game-only XBOX apply/release;
- profile Off/null release;
- AC/DC rail behavior;
- unsupported target fail-close;
- power-source failure fail-close;
- resume reinitialize-once behavior;
- uninstall factory release/fail-close;
- runtime dispose behavior;
- frontend game-profile mutation behavior.

Do not add theoretical race tests for a pure file move.

---

## 18. Review guardrails

A reviewer should treat the following as scope creep/blocking unless separately justified:

### New abstraction

Examples:

~~~text
IIntelGpuPolicyProvider
IntelGpuManager
IntelGpuService
IntelGpuSessionFactory
IntelGpuNativeProvider
GpuClockCoordinator
GpuFrequencyAuthority
~~~

Do not add them.

### Behavior edits hidden inside the split

Examples:

~~~text
changing lock boundaries
changing retry conditions
changing native result handling
changing SetRange verification
changing selectable clocks
changing startup release behavior
changing exception handling
changing log severity
changing dispose semantics
~~~

Do not do them.

### IntelFrameLimiter sharing

Do not refactor common IGCL loading between:

~~~text
IntelFrameLimiter
IntelGpuMinimumClockControl
~~~

A shared Intel IGCL abstraction is explicitly not part of this PR.

A little duplicated low-level loading code is preferable to turning a mechanical source split into an architecture rewrite.

---

## 19. Files expected to change

### Delete

~~~text
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClock.cs
~~~

### Add

~~~text
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClockPolicy.cs
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClockRuntime.cs
src/SteamInputAddonforClaw/Profiles/Performance/IntelGpuMinimumClockControl.cs
~~~

### Ideally unchanged

~~~text
AddonProcessHost.cs
InProcessAddonFrontendControl.cs
Frontend contracts
Frontend transport
UI
profiles/persistence
tests
project files
~~~

A small compiler-driven using/reference adjustment outside the three files is acceptable only if strictly required.

---

## 20. Acceptance criteria

The PR is complete only when all of the following are true:

1. The old monolithic `IntelGpuMinimumClock.cs` no longer exists.
2. `IntelGpuMinimumClockPolicy.cs` exists and owns pure policy/shared product value types.
3. `IntelGpuMinimumClockRuntime.cs` exists and owns the production lifecycle/effective policy.
4. `IntelGpuMinimumClockControl.cs` exists and owns native IGCL control/ABI types.
5. All existing production type names remain unchanged.
6. All existing relevant member signatures remain unchanged.
7. The namespace remains `SteamInputAddonforClaw.Profiles.Performance`.
8. No new manager/provider/service/interface is introduced.
9. Existing `IIntelGpuMinimumClockControl` is retained.
10. No frontend contract changes.
11. Frontend protocol remains 65.
12. Overlay protocol remains unchanged.
13. No UI changes.
14. No persistence/schema changes.
15. No product-policy changes.
16. `min = -1` factory release behavior is unchanged.
17. Maximum-frequency preservation behavior is unchanged.
18. Startup behavior is unchanged.
19. AC/DC behavior is unchanged.
20. Resume 2.5 s host settle and bounded recovery are unchanged.
21. Uninstall factory-release fail-close is unchanged.
22. Intel FPS code is unchanged.
23. Native ControlLib loading/discovery/ABI is unchanged.
24. Existing GPU tests pass without behavioral rewrites.
25. No new theoretical race machinery is added.
26. `git diff --check` passes.

---

## 21. Validation

Run at minimum:

~~~text
dotnet build SteamInputAddonforClaw.slnx --no-restore -m:1 -p:UseSharedCompilation=false

dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false

dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj --no-restore -m:1 -p:UseSharedCompilation=false

git diff --check
~~~

Also review the final diff manually.

Expected semantic diff:

~~~text
type/member bodies moved between files
+ only using/whitespace changes required by the move
~~~

If the PR contains meaningful control-flow changes, split those into a separate PR instead.

---

## 22. Final target architecture

After this PR:

~~~text
IntelGpuMinimumClockPolicy.cs
    ├─ shared semantic range/capability
    └─ pure selection / request / verification policy

IntelGpuMinimumClockRuntime.cs
    ├─ ProfileStore + active profile
    ├─ AC/DC effective target
    ├─ startup / game / resume / uninstall lifecycle
    ├─ bounded session recovery
    └─ operation/reconcile logging

IntelGpuMinimumClockControl.cs
    ├─ driver ControlLib.dll session
    ├─ Intel integrated adapter discovery
    ├─ GPU frequency domain discovery
    ├─ available clocks
    ├─ GetRange / SetRange
    └─ IGCL native ABI
~~~

The product still has one Minimum GPU Clock authority:

~~~text
Active Steam/XBOX game profile
        ↓
IntelGpuMinimumClockRuntime
        ↓
IntelGpuMinimumClockPolicy
        ↓
IntelGpuMinimumClockControl
        ↓
IGCL
~~~

This PR improves maintainability by making the already-existing responsibilities visible in the source tree without changing the working product.
