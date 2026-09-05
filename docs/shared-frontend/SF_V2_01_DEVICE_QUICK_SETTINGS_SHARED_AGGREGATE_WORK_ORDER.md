# Work Order — SF-V2-01: Device Quick Settings Shared Aggregate

> **Date:** 2026-09-05  
> **Status:** Ready for implementation  
> **Track:** Shared Frontend V2 / Phase A foundation  
> **Reviewed baseline:** `main` at `9828389f28537a0b27d46c3d9006d70f0474c49a`  
> **Architecture authority:** `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md`  
> **PR plan:** `docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md`

---

## 1. Goal

Implement the first Shared Frontend V2 foundation PR.

Create one typed Device Quick Settings aggregate over the three Runtime-owned Device features that are already consumed together by the desktop Main UI and Steam QAM:

```text
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
        ↓
InProcessAddonFrontendControl
        ↓
FrontendDeviceQuickSettingsSnapshot
        ↓
┌──────────────────────┬──────────────────────┐
│ .Frontend / Main UI  │ .Qam / QamHost       │
└──────────────────────┴──────────────────────┘
```

Then migrate the current Main UI Device refresh and QAM no-active-game Device refresh to that shared aggregate.

This PR is **foundation only**.

It must not add real Device feature transport to the Addon Overlay yet. That is `SF-V2-02` / Phase B.

The design target is:

> **One Runtime authority per feature, one typed shared Device projection, and no duplicate state/hardware ownership.**

---

## 2. Baseline verification

This work order was re-checked against current `main` immediately before writing.

Current baseline:

```text
9828389f28537a0b27d46c3d9006d70f0474c49a
```

The Shared Frontend V2 architecture/plan were originally reviewed against `b97c012156b3734ce2230f7e469db91aad94b784`.

The commits between that baseline and current `main` only reorganized the `docs/shared-frontend/` documentation:

```text
old SHARED_FRONTEND_01 work order          → deleted
old surface-exposure addendum              → deleted
SHARED_FRONTEND_ARCHITECTURE_V2.md         → added
SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2  → added
```

No production source changed in that interval.

Therefore the current production-code facts described below are still valid against latest `main`.

The two deleted legacy Shared Frontend documents are **not required reading** and must not be used as implementation authority.

---

## 3. Required reading before implementation

Read current source and current design authority, not historical snippets only.

### Full PID1902 authority — current precedence

Read:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
```

Relevant invariant:

```text
Main UI / QAM / Overlay = disposable frontend surfaces
Runtime               = Device/controller feature authority
```

This PR must not alter:

```text
Center M Enabled/Disabled controller authority
PID1901 / PID1902 ownership
DirectInput physical ownership
HidHide deterministic baseline
VIIPER ownership / teardown
Xbox360 / SteamDeck presentation selection
PnP recovery
sleep / hibernate / resume handling
restart / shutdown teardown
Win+G suppression
stock restoration
OQ4 Overlay controller capture safety
```

### Shared Frontend V2

Read:

```text
docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
```

### Current app / Overlay design context

Read the relevant current documents:

```text
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
docs/work-order/APP_UI_PR_A_NAVIGATION_AND_PAGE_OWNERSHIP_REORGANIZATION_WORK_ORDER.md
docs/work-order/APP_UI_PR_B_LEGACY_STATUS_AND_STARTUP_PREFERENCE_CLEANUP_WORK_ORDER.md
docs/work-order/APP_UI_PR_C_FRONT_BUTTON_MAPPING_AND_OVERLAY_ACTION_WORK_ORDER.md
docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md
```

The purpose is only to preserve the current ownership boundaries. Do not broaden this PR into App UI or Overlay redesign.

### Current source — minimum review set

Inspect at minimum:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs
src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
```

And relevant tests, especially:

```text
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
existing Device frontend/runtime tests
existing Overlay transport/OQ4 tests
```

---

## 4. Current source facts that must drive the implementation

### 4.1 One Runtime/frontend projection already exists

`AddonProcessHost` constructs one production:

```text
InProcessAddonFrontendControl
```

and stores it as:

```text
_frontendControl
```

The same object receives the existing Runtime-owned feature instances, including:

```text
_cpuBoostRuntime
_tdpRuntime
_powerModeRuntime
```

Do not create another Device/Quick Settings authority.

### 4.2 Main UI and QAM already use the same Runtime truth

Current composition is:

```text
same _frontendControl
      │
      ├─ .Frontend → NamedPipeAddonFrontendServer → Main UI
      │
      └─ .Qam      → NamedPipeAddonFrontendServer → QamHost
```

The two pipe endpoints differ only because the frontend process lifetimes differ.

They do not represent separate CPU/TDP/Power authorities.

### 4.3 Current common frontend protocol is v25

Current source:

```csharp
FrontendTransportProtocol.CurrentVersion = 25;
```

`FrontendRpcMethod` has the individual methods:

```text
CaptureCpuBoost
CaptureTdp
CapturePowerMode
```

but no aggregate capture yet.

### 4.4 Main UI still performs three Device reads

Current `DevicePage.RefreshAsync()` separately invokes:

```text
CaptureCpuBoostAsync()
CaptureTdpAsync()
CapturePowerModeAsync()
```

`RefreshCenterMStartupAsync()` is already separate and must remain separate.

### 4.5 QAM still performs the same three Device reads

Current no-active-game QAM refresh performs:

```javascript
captureCpuBoost
capturePowerMode
captureTdp
```

through `QamFrontendBridge`.

Current QAM mutation admission remains:

```text
Steam active
AND no running game
AND Steam source == BigPicture
```

inside `QamFrontendBridge.MutateAsync(...)`.

That is QAM surface policy and must remain there.

### 4.6 Overlay is intentionally separate

Current Overlay protocol remains:

```text
OverlayTransportProtocol.CurrentVersion = 5
```

and carries its own lifecycle/navigation/tab-order traffic.

This PR must not touch the Overlay wire or Overlay production UI.

---

## 5. Supported frontend surfaces for SF-V2-01

Freeze the exposure scope explicitly.

```text
Desktop Main UI: Yes
Steam QAM:       Yes
Addon Overlay:   No — transport is SF-V2-02
```

The typed aggregate is intentionally suitable for future Overlay use, but this PR does not expose it through `.Overlay`.

Do not infer any additional surface exposure from the existence of the shared DTO.

---

## 6. Add the typed Device aggregate contract

Add one record in the existing frontend contract namespace.

Required shape:

```csharp
public sealed record FrontendDeviceQuickSettingsSnapshot(
    FrontendCpuBoostSnapshot CpuBoost,
    FrontendTdpSnapshot Tdp,
    FrontendPowerModeSnapshot PowerMode)
{
    public static readonly FrontendDeviceQuickSettingsSnapshot Unavailable = new(
        FrontendCpuBoostSnapshot.Unavailable,
        FrontendTdpSnapshot.Unavailable,
        FrontendPowerModeSnapshot.Unavailable);
}
```

Use the existing child DTOs exactly.

Do not flatten them into another shape.

Do not add:

```text
Center M authority
FrontendStatusSnapshot
active Game Profile
front-button mapping
component diagnostics
fan control placeholder
battery limit placeholder
LED placeholder
vibration placeholder
```

### Why Center M stays separate

Center M happens to live on the Device page, but it is a reboot-bound controller-authority transition, not an ordinary Device quick setting.

Keep its existing contracts separate:

```text
CaptureCenterMStartupAsync
RequestCenterMAuthorityTransitionAsync
```

No QAM/Overlay Center M exposure is approved by this PR.

---

## 7. Add one aggregate read seam to `IAddonFrontendControl`

Add:

```csharp
Task<FrontendDeviceQuickSettingsSnapshot> CaptureDeviceQuickSettingsAsync(
    CancellationToken cancellationToken = default)
    => Task.FromResult(FrontendDeviceQuickSettingsSnapshot.Unavailable);
```

Keep the existing focused methods:

```text
CaptureCpuBoostAsync
CaptureTdpAsync
CapturePowerModeAsync
```

Keep all existing feature-specific mutation methods.

Do not remove or alias those methods in this PR.

The aggregate is a shared UI projection convenience, not a replacement frontend framework.

---

## 8. Implement aggregate capture in `InProcessAddonFrontendControl`

Use the same existing Runtime instances and the same existing mapping functions.

Conceptually:

```text
_cpuBoostRuntime.Snapshot
→ existing MapCpuBoostSnapshot(...)

_tdpRuntime.CaptureSnapshot()
→ existing MapTdpSnapshot(...)

_powerModeRuntime.Snapshot
→ existing MapPowerModeSnapshot(...)

→ FrontendDeviceQuickSettingsSnapshot
```

### 8.1 Admission semantics

At aggregate entry:

```csharp
ThrowIfShuttingDown();
cancellationToken.ThrowIfCancellationRequested();
```

After that, perform the three normal read-only child captures sequentially.

Do not add a cross-feature lifetime gate, epoch, transaction, or barrier to make all three reads simultaneous.

The supported UI only needs an authoritative near-time snapshot and already converges through `StateInvalidated`.

### 8.2 Feature-local failure isolation

A real capture failure in one child must not discard healthy siblings.

Required behavior:

```text
CPU capture fails
→ CpuBoost = FrontendCpuBoostSnapshot.Unavailable
→ TDP and Power Mode still captured

TDP capture fails
→ Tdp = FrontendTdpSnapshot.Unavailable
→ CPU and Power Mode still returned

Power Mode capture fails
→ PowerMode = FrontendPowerModeSnapshot.Unavailable
→ CPU and TDP still returned
```

Use explicit, local `try/catch` blocks around each child read.

Log the failing feature through the existing app logging style.

Do not catch/suppress `OperationCanceledException` into an `Unavailable` child.

Do not turn process shutdown admission into a healthy aggregate.

### 8.3 Read-only means read-only

`CaptureDeviceQuickSettingsAsync` must not:

- persist profile/settings data;
- invoke Device mutation methods;
- call reconcile/apply solely because state was requested;
- raise `StateInvalidated` solely because a snapshot was read;
- create a hardware reader;
- create a cached aggregate;
- touch controller presentation or controller authority.

### 8.4 No speculative parallelization

Do not introduce `Task.WhenAll`, a worker queue, or parallel hardware-read machinery for these three current projections.

Keep the implementation simple and deterministic.

If a future hardware-backed feature makes aggregate latency measurable, optimize in a separate evidence-driven PR.

---

## 9. Frontend transport — add one explicit no-payload RPC

### 9.1 Protocol version

This PR changes the desktop/QAM frontend wire contract.

Change:

```text
FrontendTransportProtocol
25 → 26
```

Add a protocol history comment describing v26, for example:

```text
Version 26: Shared Frontend V2 adds CaptureDeviceQuickSettings and the typed
FrontendDeviceQuickSettingsSnapshot aggregate used by Main UI/QAM Device refresh.
```

Because the product is pre-release, an old v25 peer must fail the handshake rather than receive a late unsupported-method failure.

Do not change historical version comments.

### 9.2 RPC method

Add:

```text
FrontendRpcMethod.CaptureDeviceQuickSettings
```

It takes no payload.

Do not add a generic method such as:

```text
CaptureFeature(string name)
GetSetting(string key)
InvokeFeature(...)
```

### 9.3 Server payload validation

`NamedPipeAddonFrontendServer` has an explicit no-payload allowlist/validation condition.

Add `CaptureDeviceQuickSettings` to that existing condition.

Required behavior:

```text
CaptureDeviceQuickSettings + payload present
→ InvalidMessage
→ frontend operation NOT invoked
```

Do not refactor the current explicit dispatcher into reflection, a handler registry, or a service container for this one method.

### 9.4 Server dispatch

Add one explicit dispatch branch to:

```csharp
_inner.CaptureDeviceQuickSettingsAsync(t)
```

Serialize the returned typed aggregate with the existing wire codec.

Preserve current request cancellation, operation gate, frame-size, disconnect, and error semantics.

### 9.5 Client

Add the typed client method:

```csharp
public Task<FrontendDeviceQuickSettingsSnapshot> CaptureDeviceQuickSettingsAsync(
    CancellationToken t = default) =>
    SendAsync<FrontendDeviceQuickSettingsSnapshot>(
        FrontendRpcMethod.CaptureDeviceQuickSettings,
        null,
        t);
```

No new transport class is required.

---

## 10. Main UI migration

Change only the normal Device-page CPU/TDP/Power refresh path.

Target:

```text
DevicePage.RefreshAsync()
        ↓
CaptureDeviceQuickSettingsAsync()   // exactly one normal Device aggregate read
        ↓
Render(snapshot.CpuBoost)
RenderTdp(snapshot.Tdp)
RenderPowerMode(snapshot.PowerMode)
```

### 10.1 Keep Center M separate

Do not fold:

```text
RefreshCenterMStartupAsync()
```

into the aggregate.

Its existing page-entry refresh reason and reboot-bound lifecycle semantics remain unchanged.

### 10.2 Preserve current per-feature UI behavior

Do not redesign Device UI.

Preserve:

- CPU Boost desired/current rendering;
- CPU Boost Enabled behavior;
- TDP limits;
- TDP local dirty-draft preservation during ordinary authoritative refresh;
- Power Mode current/desired rendering;
- mutation-result authoritative readback;
- existing InfoBar style;
- existing `StateInvalidated` subscription/refresh behavior.

### 10.3 Valid partial aggregate

A valid aggregate may contain:

```text
CpuBoost = available
Tdp = unavailable
PowerMode = available
```

Render each child independently.

Do not add an aggregate-wide `Available` flag.

### 10.4 Whole transport failure must fail closed

If the aggregate RPC itself fails because the Runtime/frontend transport is unavailable, do not leave stale Device controls looking editable.

Use the existing Device page style to render all three children unavailable and show concise load failure feedback.

For TDP, ensure an old draft is not presented as authoritative editable state after the whole transport has failed.

Do not add a global page error framework or Device state cache.

---

## 11. Steam QAM migration

### 11.1 Add one bridge read operation

Add exactly:

```text
captureDeviceQuickSettings
```

in `QamFrontendBridge`, mapped to:

```csharp
_client.CaptureDeviceQuickSettingsAsync(token)
```

### 11.2 Migrate the no-active-game refresh path

Current:

```javascript
const nextCpu = activeGame ? null : await request("captureCpuBoost");
const nextPowerMode = activeGame ? null : await request("capturePowerMode");
const nextTdp = activeGame ? null : await request("captureTdp");
```

Target conceptually:

```javascript
const nextDevice = activeGame
  ? null
  : await request("captureDeviceQuickSettings");

const nextCpu = nextDevice?.cpuBoost ?? null;
const nextPowerMode = nextDevice?.powerMode ?? null;
const nextTdp = nextDevice?.tdp ?? null;
```

Use the actual existing camelCase bridge serialization convention.

### 11.3 Keep Status and active Profile separate

Do not fold these into the Device aggregate:

```text
captureStatus
captureActiveGameProfile
```

They are different scopes and have different QAM responsibilities.

### 11.4 Preserve all current QAM interaction policy

This PR changes only where the three Device read snapshots come from.

Preserve:

```text
refreshInFlight / refreshDirty
mutationDepthRef / deferredInvalidationRef
activeProfileAppIdRef handling
2-second QAM slider commit delay
pending CPU/TDP/Power drafts
failClosed behavior
active-game Profile path
no polling loop
```

Most importantly, preserve `QamFrontendBridge.MutateAsync(...)` admission exactly:

```text
Steam active
AND AppId == 0
AND Source == BigPicture
```

Do not move that gate into the shared aggregate or Runtime feature owners.

### 11.5 Retire obsolete QAM read adapter cases only

After `qam.js` has no caller for:

```text
captureCpuBoost
captureTdp
capturePowerMode
```

remove those three **QAM string bridge read cases** so QAM has one Device read path.

Do **not** remove the common typed individual methods/RPCs from:

```text
IAddonFrontendControl
NamedPipeAddonFrontendClient
NamedPipeAddonFrontendServer
InProcessAddonFrontendControl
```

Those focused contracts remain valid.

---

## 12. Expected production footprint

Likely production files:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs
src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
```

Focused test changes belong under:

```text
tests/SteamInputAddonforClaw.Tests/
```

Expected size is roughly the planned Shared Frontend foundation range:

```text
~250–500 LOC changed/added when practical
```

This is not a hard limit.

If the implementation starts requiring a new project, manager hierarchy, cache, new named pipe, generic RPC layer, or controller lifecycle edits, stop and reassess for scope drift/overengineering.

---

## 13. Required tests

### 13.1 Aggregate contract

Verify:

```text
FrontendDeviceQuickSettingsSnapshot.Unavailable.CpuBoost
== FrontendCpuBoostSnapshot.Unavailable

FrontendDeviceQuickSettingsSnapshot.Unavailable.Tdp
== FrontendTdpSnapshot.Unavailable

FrontendDeviceQuickSettingsSnapshot.Unavailable.PowerMode
== FrontendPowerModeSnapshot.Unavailable
```

Use the existing typed child contracts; no dynamic feature collection should exist.

### 13.2 In-process aggregate capture

Add focused tests proving:

- CPU Boost maps through the current `MapCpuBoostSnapshot` semantics;
- TDP maps through the current `MapTdpSnapshot` semantics;
- Power Mode maps through the current `MapPowerModeSnapshot` semantics;
- a missing CPU authority produces only CPU `Unavailable`;
- a missing TDP authority produces only TDP `Unavailable`;
- a missing Power Mode authority produces only Power Mode `Unavailable`;
- a real exception from one child capture does not discard healthy siblings;
- cancellation/shutdown admission is not converted into a healthy aggregate;
- aggregate capture does not persist/apply/reconcile;
- aggregate capture does not raise `StateInvalidated` merely because it was read.

Use existing test seams/fakes where possible.

If inducing one child exception requires a test seam, use the smallest local seam consistent with existing test style. Do not introduce a production manager/interface hierarchy solely to inject this failure.

### 13.3 Frontend named-pipe transport

Extend `FrontendNamedPipeTransportTests` or the nearest existing transport tests to prove:

- `CaptureDeviceQuickSettings` round-trips the complete typed aggregate;
- request payload is `null` in normal client use;
- unexpected payload returns `InvalidMessage`;
- unexpected payload invokes zero aggregate frontend operations;
- `FrontendTransportProtocol.CurrentVersion == 26`;
- an old v25 client is rejected during handshake;
- current protocol JSON fixtures that intentionally mean "current" are updated from 25 to 26;
- historical old-version rejection tests retain their historical values;
- existing cancellation/disconnect behavior remains green.

Update `RecordingFrontendControl` with one aggregate result/call counter rather than creating a second transport fake framework.

### 13.4 Main UI Device refresh

Use the repository's existing source/contract-test style where practical.

Prove that normal `DevicePage.RefreshAsync()`:

```text
calls CaptureDeviceQuickSettingsAsync once
```

and no longer performs the three separate Device capture calls in that normal refresh path.

Also preserve:

- Center M separate refresh;
- TDP dirty-draft behavior;
- feature-specific rendering;
- transport-level fail-closed rendering.

Do not add full WinUI automation solely for this wiring change if current source-contract tests are sufficient.

### 13.5 QAM contract tests

Update `QamFrontendContractTests` and related tests to prove:

```text
qam.js uses captureDeviceQuickSettings for no-active-game Device state
```

and no longer independently requests:

```text
captureCpuBoost
captureTdp
capturePowerMode
```

for Device refresh.

Also prove the existing behavior remains:

- `captureStatus` remains separate;
- `captureActiveGameProfile` remains separate;
- active-game Profile state is unchanged;
- QAM mutation admission remains Big Picture + no running game;
- 2-second delayed slider commit remains;
- pending drafts survive ordinary invalidation as before;
- `refreshInFlight` / `refreshDirty` behavior remains;
- no `setInterval` polling is introduced.

### 13.6 Overlay regression

Overlay production files should not change in this PR.

Run the existing Overlay regression tests, especially:

```text
Overlay transport handshake
OQ4 capture/navigation/session-loss behavior
tab-order transport
row/shortcut selection
show/hide lifecycle
```

The Frontend protocol v26 change must not alter `.Overlay` protocol v5 behavior.

---

## 14. Explicit non-goals

Do not implement any of the following in `SF-V2-01`.

### Overlay feature work

```text
no OverlayTransportProtocol v6
no Device snapshot on .Overlay
no Overlay Device mutation messages
no Overlay CPU/TDP/Power UI binding
no Overlay feature state store/cache
```

### New transport architecture

```text
no new named pipe
no .Overlay.Feature endpoint
no third full NamedPipeAddonFrontendServer for Overlay
no multi-client generic frontend bus
no reflection/generic RPC dispatcher
```

### New authority/state framework

```text
no QuickSettingsRuntime
no SharedFrontendManager
no DeviceSettingsManager
no SharedDeviceStateCache
no feature registry
no surface matrix registry
no event bus
no aggregate persistence
no cross-feature lock / epoch / barrier / transaction
```

### Unrelated features

```text
no Center M authority changes
no Profile contract redesign
no front-button mapping changes
no Battery Charge Limit
no fan control
no LED control
no vibration-strength feature
no diagnostics redesign
```

### Full1902/controller lifecycle

No behavioral changes to:

```text
PID1901/PID1902
HidHide
DirectInput
VIIPER
presentation selection
PnP recovery
sleep/hibernate/resume
restart/shutdown
Win+G suppression
stock restoration
OQ4 capture/release
```

---

## 15. Verification

Before marking the PR complete, run:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Requirements:

```text
Debug build:   PASS, 0 new warnings/errors
Release build: PASS, 0 new warnings/errors
Full tests:    PASS
diff check:    clean
```

No new hardware behavior is introduced by this PR, so new MSI Claw hardware validation is not a completion requirement for SF-V2-01.

A basic UI/QAM smoke test is useful if available, but the core acceptance criterion is that behavior remains functionally identical while the read path becomes shared.

---

## 16. Acceptance checklist

### Architecture

- [ ] exactly one existing `InProcessAddonFrontendControl` remains the Runtime/frontend projection boundary;
- [ ] no new Device/Quick Settings authority exists;
- [ ] no new hardware reader/cache/state store exists;
- [ ] `.Frontend` and `.Qam` still point to the same `_frontendControl`;
- [ ] `.Overlay` protocol remains v5 and production Overlay source is unchanged;
- [ ] Full1902/controller lifecycle is untouched.

### Contract

- [ ] `FrontendDeviceQuickSettingsSnapshot` exists;
- [ ] it contains only typed CPU Boost/TDP/Power Mode children;
- [ ] `Unavailable` contains all three child unavailable snapshots;
- [ ] `IAddonFrontendControl.CaptureDeviceQuickSettingsAsync` exists;
- [ ] individual CPU/TDP/Power capture/mutation contracts remain.

### Runtime projection

- [ ] aggregate reuses current Runtime authorities/mappers;
- [ ] aggregate is read-only;
- [ ] one child failure is feature-local;
- [ ] cancellation/shutdown is not fabricated as healthy state;
- [ ] no `StateInvalidated` is emitted merely for reading.

### Frontend transport

- [ ] `FrontendTransportProtocol` is 26;
- [ ] `CaptureDeviceQuickSettings` is an explicit no-payload RPC;
- [ ] unexpected payload is rejected before invocation;
- [ ] server and client typed paths round-trip correctly;
- [ ] v25 peer fails handshake;
- [ ] no generic RPC abstraction was added.

### Main UI

- [ ] normal Device refresh performs one aggregate capture;
- [ ] Center M remains separate;
- [ ] partial child unavailability renders independently;
- [ ] whole transport failure leaves no stale editable Device controls;
- [ ] TDP dirty draft behavior remains correct during ordinary refresh.

### QAM

- [ ] no-active-game Device refresh uses `captureDeviceQuickSettings`;
- [ ] old QAM-only individual read bridge cases are removed once unused;
- [ ] common individual frontend RPCs remain;
- [ ] Status/Profile reads remain separate;
- [ ] QAM mutation admission remains unchanged;
- [ ] slider/debounce/draft/invalidation behavior remains unchanged;
- [ ] no polling was added.

### Validation

- [ ] Debug build passes;
- [ ] Release build passes;
- [ ] full Release test suite passes;
- [ ] `git diff --check` is clean;
- [ ] no new warnings.

---

## 17. Review standard

Review this PR for realistic production regressions, especially:

- one child failure incorrectly discarding healthy sibling state;
- stale editable Main UI/QAM controls after real transport failure;
- protocol v25/v26 mismatch handling errors;
- malformed no-payload request reaching Runtime;
- QAM-only mutation policy leaking into shared Runtime semantics;
- accidental deletion/change of focused CPU/TDP/Power contracts;
- duplicate Runtime/hardware authority;
- accidental `.Overlay` or Full1902 lifecycle changes;
- real cancellation/disconnect/resource regressions in the existing frontend pipe.

Do **not** block for theoretical timing differences between the three adjacent read-only child captures.

Do not add locks, epochs, barriers, revision vectors, queues, or a generalized state manager merely to make a UI aggregate transactionally simultaneous.

The supported product converges through the existing Runtime authorities and low-rate `StateInvalidated` refresh model.

---

## 18. Next PR boundary

After `SF-V2-01` is merged and green, prepare/implement:

```text
SF-V2-02 — Overlay Device Quick Settings Transport
```

That PR will:

```text
reuse FrontendDeviceQuickSettingsSnapshot
reuse existing CPU/TDP/Power mutation semantics
extend the existing .Overlay endpoint only
bump Overlay protocol 5 → 6
preserve OQ4 controller capture/show/close safety
```

Do not pre-implement any of that work in SF-V2-01.
