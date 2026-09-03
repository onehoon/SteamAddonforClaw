# Work Order — Shared Frontend 01: Device Quick Settings Snapshot Contract

## Status

Foundation PR for the shared Runtime → frontend Quick Settings contract.

Track: Shared Frontend / Quick Settings foundation  
Label/name: `SHARED-FRONTEND-01`  
Implementation baseline:

```text
main
6bcc57e5e29020e8d242e36371c2611531f0bb45
```

This work order is intentionally placed outside `docs/overlayui/` because the contract is **not Overlay-owned**. It is a shared Runtime/frontend projection intended for the existing desktop Main UI, Steam QAM, and the Addon Overlay.

This PR is a prerequisite/foundation for the later Overlay Device feature-binding work. It does **not** bind a TDP/CPU Boost/Power Mode control into the Overlay yet.

---

## 1. Goal

Create one typed **Device Quick Settings aggregate snapshot** that projects the already-existing Runtime-owned Device/global feature authorities:

```text
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
        ↓
InProcessAddonFrontendControl
        ↓
FrontendDeviceQuickSettingsSnapshot
```

Then make the two existing consumers that already read all three Device features use that aggregate contract:

```text
Main UI Device page
Steam QAM Device scope
```

The Addon Overlay will consume the **same typed contract** in the next focused transport PR through the existing `.Overlay` endpoint.

The desired architecture after this PR is:

```text
                         Addon Runtime
                              │
       ┌──────────────────────┼──────────────────────┐
       │                      │                      │
 CpuBoostRuntime          TdpRuntime          PowerModeRuntime
       │                      │                      │
       └──────────────────────┼──────────────────────┘
                              │
                InProcessAddonFrontendControl
                              │
             FrontendDeviceQuickSettingsSnapshot
                              │
               ┌──────────────┴──────────────┐
               │                             │
        .Frontend / Main UI            .Qam / QamHost
               │                             │
        DevicePage refresh                qam.js refresh

Future focused PR:
               │
               └──────── existing .Overlay endpoint
                          ↓
                     Addon Overlay
```

This PR must **not** create another Device/Quick Settings authority, another hardware reader, another state cache, another long-lived manager, or another named-pipe endpoint.

---

## 2. Why this foundation is needed now

The repository already has the correct core ownership model:

```text
Runtime feature authority
→ IAddonFrontendControl
→ typed frontend contracts
→ disposable UI surfaces
```

However, the Main UI Device page and Steam QAM currently assemble the same Device view through multiple individual capture calls.

Current Main UI shape:

```text
CaptureCpuBoostAsync()
CaptureTdpAsync()
CapturePowerModeAsync()
```

Current QAM device-scope refresh shape:

```text
captureStatus
captureActiveGameProfile

if no active game:
    captureCpuBoost
    capturePowerMode
    captureTdp
```

The upcoming Addon Overlay Device tab needs the same Runtime truth.

Do **not** solve that by building a third set of Overlay-specific DTOs/managers/readers.

Instead, freeze one narrow typed Device aggregate now so:

- Main UI consumes it;
- QAM consumes it;
- Overlay can consume the same DTO next;
- future Device features such as Battery Charge Limit can extend the same typed aggregate after their own Runtime contract exists.

---

## 3. Required reading before implementation

Read current `main`, not historical snippets only.

### Full PID1902 authority documents

Read in current precedence order:

1. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
2. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
3. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
4. current `docs/work-order/` set

Relevant invariant:

> Frontends are disposable clients. Runtime remains the controller/device authority. This PR must not alter controller authority, PID1902 ownership, HidHide, VIIPER, presentation selection, recovery, or OQ4 capture safety.

### Overlay documents

- `docs/overlayui/README.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/work-order/OQ_POC_B_OVERLAY_TRANSPORT_WARM_LIFECYCLE_WORK_ORDER.md`
- `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`

Important existing Overlay transport rule from OQ-POC-B:

```text
Do not attach a third full NamedPipeAddonFrontendServer merely for Overlay.
Do not make .Overlay a copy of the entire IAddonFrontendControl RPC surface.
Future Quick Settings feature work should extend the existing narrow .Overlay endpoint.
```

Therefore **do not add `.Overlay.Feature` or another feature pipe in this PR**.

### Future Device feature reference

Read:

- `docs/RE_MSI_BatteryChargeLimit.md`

This document proves a future Battery Charge Limit feature has a distinct WMI/ACPI hardware contract and still requires product/hardware validation. It is relevant only as an **extensibility example** for the shared typed snapshot.

Do not implement Battery Charge Limit in this PR.

### Required source review

At minimum inspect current:

- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`
- `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
- `src/SteamInputAddonforClaw.UI/App.xaml.cs`
- `src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs`
- `src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs`
- `src/SteamInputAddonforClaw.QamHost/Frontend/qam.js`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- relevant frontend/QAM/Overlay tests

---

## 4. Current architecture facts that must be preserved

### 4.1 There is already one common Runtime frontend authority

`AddonProcessHost` constructs one:

```text
InProcessAddonFrontendControl
```

and stores it as the shared `_frontendControl`.

That one object already projects the Runtime-owned feature instances, including:

```text
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
GameProfileMutations
IntelFrameLimiterRuntime
CenterMStartupControl
...
```

Do not create:

```text
QuickSettingsRuntime
QuickSettingsManager
DeviceSettingsManager
OverlayDeviceManager
QamDeviceManager
SharedDeviceStateCache
```

The existing `InProcessAddonFrontendControl` is the common Runtime/frontend projection boundary.

### 4.2 Desktop Main UI and QAM already share the same Runtime feature instance

Current transport composition is conceptually:

```text
.Frontend
→ NamedPipeAddonFrontendServer
→ same _frontendControl

.Qam
→ NamedPipeAddonFrontendServer
→ same _frontendControl
```

The endpoints are separate because the processes/lifetimes are separate.

The feature authority is not separate.

This PR must preserve that model.

### 4.3 QAM-specific policy must remain QAM-specific

`QamFrontendBridge` currently applies a QAM-only mutation gate for Device/global mutation, including the Big Picture / no-running-game condition.

That is **presentation policy**, not shared Runtime feature semantics.

Do not move QAM-specific eligibility into:

- `IAddonFrontendControl`;
- the new aggregate snapshot;
- `CpuBoostRuntime`;
- `TdpRuntime`;
- `PowerModeRuntime`;
- a generic frontend policy layer.

Main UI and future Overlay must not inherit a QAM-only restriction merely because they share the same snapshot DTO.

### 4.4 Overlay transport is intentionally different

`.Overlay` currently owns:

```text
Handshake / Ready
Show / Visible
Hide / Hidden
Shutdown
semantic controller Navigation
DismissRequested
TabOrderState / SetTabOrder
```

It is a lifecycle/control protocol with OQ4 safety implications.

This PR must not alter it.

Specifically:

```text
OverlayTransportProtocol.CurrentVersion stays 5
OverlayWireMessageKind stays unchanged
NamedPipeOverlayServer stays unchanged
NamedPipeOverlayClient stays unchanged
```

The next focused PR will decide the narrow `.Overlay` message required to carry the shared Device snapshot.

---

## 5. New shared typed contract

Add one new frontend contract in the existing frontend contract namespace.

Recommended shape:

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

Exact formatting may follow current file style.

The important contract is:

```text
Device Quick Settings aggregate
├─ CPU Boost typed child snapshot
├─ TDP typed child snapshot
└─ Windows Power Mode typed child snapshot
```

Do not flatten the existing child contracts into a new duplicate shape.

Wrong:

```csharp
record QuickSettingItem(string Name, object Value, bool Available);
Dictionary<string, JsonElement> Features;
Dictionary<string, object> DeviceSettings;
```

Wrong:

```text
new OverlayCpuSnapshot
new QamTdpSnapshot
new MainUiPowerSnapshot
```

The existing feature-specific records remain the canonical frontend projections.

---

## 6. Add one aggregate capture to `IAddonFrontendControl`

Add conceptually:

```csharp
Task<FrontendDeviceQuickSettingsSnapshot> CaptureDeviceQuickSettingsAsync(
    CancellationToken cancellationToken = default)
    => Task.FromResult(FrontendDeviceQuickSettingsSnapshot.Unavailable);
```

Keep the existing individual capture methods:

```text
CaptureCpuBoostAsync
CaptureTdpAsync
CapturePowerModeAsync
```

Do not delete them merely because this aggregate now exists.

Reasons:

- mutation results already return feature-specific snapshots;
- focused feature operations remain useful;
- a later real requirement may need targeted capture;
- removing them adds unrelated churn to this foundation PR.

This PR adds one shared aggregate seam; it does not redesign the entire frontend interface.

---

## 7. Runtime implementation: aggregate existing authorities only

Implement `CaptureDeviceQuickSettingsAsync` on `InProcessAddonFrontendControl` using the **same existing Runtime-owned feature instances and existing mapping semantics**.

Conceptually:

```text
CaptureDeviceQuickSettingsAsync
    ↓
CPU Boost → existing Runtime snapshot → existing frontend mapping
TDP       → existing Runtime snapshot → existing frontend mapping
PowerMode → existing Runtime snapshot → existing frontend mapping
    ↓
FrontendDeviceQuickSettingsSnapshot
```

Do not instantiate any new:

- Windows power reader;
- TDP transport;
- MSI helper;
- WMI reader;
- registry reader;
- ProfileStore;
- hardware device object.

Do not persist anything from this read operation.

Do not trigger reconcile/apply merely because a frontend requested a snapshot.

### 7.1 Preserve feature-local failure isolation

This point is required.

Today `DevicePage.RefreshAsync()` captures CPU Boost, TDP, and Power Mode independently. A failure while capturing one feature does not inherently prevent the other two from being rendered.

Do not regress to:

```text
TDP capture throws
→ entire Device aggregate request fails
→ CPU Boost and Power Mode disappear too
```

The aggregate must preserve feature-local failure behavior.

Preferred simple policy:

```text
aggregate capture admitted

CPU capture fails
→ log CPU capture failure at Runtime/frontend projection boundary
→ CpuBoost = FrontendCpuBoostSnapshot.Unavailable
→ continue

TDP capture fails
→ log TDP capture failure
→ Tdp = FrontendTdpSnapshot.Unavailable
→ continue

Power Mode capture fails
→ log Power Mode capture failure
→ PowerMode = FrontendPowerModeSnapshot.Unavailable
→ continue
```

A Runtime-wide condition such as the frontend control already being in process shutdown may still reject the aggregate request through the existing shutdown contract.

Do not catch/suppress cancellation or shutdown solely to fabricate a healthy snapshot.

### 7.2 No aggregate transaction/epoch/lock

The snapshot is a UI projection, not a hardware transaction.

Do not add:

```text
QuickSettingsSnapshotLock
FeatureSnapshotEpoch
cross-feature transaction
barrier
version vector
atomic read coordinator
```

The three Runtime authorities already own their own state synchronization.

A normal capture may observe child snapshots from immediately adjacent moments. That is acceptable for ordinary Quick Settings UI and does not justify new global synchronization.

### 7.3 Do not parallelize hardware work speculatively

Current captures are cheap Runtime projections. A future feature such as Battery Charge Limit may involve WMI/ACPI reads.

Do not pre-build `Task.WhenAll`/parallel hardware fan-out machinery solely for that future possibility.

Keep the capture simple and deterministic. Optimize only if measured latency later requires it.

---

## 8. Frontend wire contract

The existing desktop/QAM transport must carry the aggregate because both of those consumers use `NamedPipeAddonFrontendClient`.

### 8.1 Protocol version

Current:

```text
FrontendTransportProtocol.CurrentVersion = 20
```

Change to:

```text
FrontendTransportProtocol.CurrentVersion = 21
```

Add a version comment explaining that v21 adds the typed shared Device Quick Settings aggregate capture RPC.

This is a pre-release application. Prefer an honest handshake failure between old/new peers rather than late `UnsupportedMethod` behavior.

Do **not** change:

```text
OverlayTransportProtocol.CurrentVersion = 5
```

in this PR.

### 8.2 RPC method

Add one explicit RPC method:

```text
CaptureDeviceQuickSettings
```

No request payload is required.

Do not add a generic:

```text
CaptureFeature(string name)
GetSetting(string key)
InvokeFeature(...)
```

### 8.3 Server

Extend `NamedPipeAddonFrontendServer` narrowly:

- recognize `CaptureDeviceQuickSettings` as a no-payload request;
- dispatch explicitly to `_inner.CaptureDeviceQuickSettingsAsync(...)`;
- serialize the typed result;
- preserve existing request cancellation, operation gate, frame bounds, and error behavior.

Do not refactor the explicit RPC dispatcher into reflection, a generic handler registry, or a service container solely for one new method.

### 8.4 Client

Add one typed method to `NamedPipeAddonFrontendClient`:

```csharp
public Task<FrontendDeviceQuickSettingsSnapshot> CaptureDeviceQuickSettingsAsync(
    CancellationToken t = default)
    => SendAsync<FrontendDeviceQuickSettingsSnapshot>(
        FrontendRpcMethod.CaptureDeviceQuickSettings,
        null,
        t);
```

Follow current naming/style exactly.

---

## 9. Main UI migration

Migrate only the Device-page refresh read path.

Current shape:

```text
CaptureCpuBoostAsync
→ Render CPU

CaptureTdpAsync
→ Render TDP

CapturePowerModeAsync
→ Render Power Mode
```

New shape:

```text
CaptureDeviceQuickSettingsAsync
        ↓
Render(snapshot.CpuBoost)
RenderTdp(snapshot.Tdp)
RenderPowerMode(snapshot.PowerMode)
```

Requirements:

- one aggregate capture per normal Device-page refresh;
- keep `StateInvalidated` refresh behavior;
- keep current CPU/TDP/Power mutation methods;
- keep mutation-result authoritative readback behavior;
- keep existing TDP local dirty-draft preservation;
- keep existing UI controls/layout unchanged;
- do not make DevicePage a cache authority.

### 9.1 Aggregate transport failure

If the entire Runtime/frontend connection fails, render a safe unavailable state rather than leaving stale controls looking editable.

Use the existing page error/UI style.

Do not invent a new global error framework in this PR.

### 9.2 Partial feature failure

A valid aggregate may contain:

```text
CpuBoost = available
Tdp = unavailable
PowerMode = available
```

The page must render those states independently.

The aggregate is not an all-or-nothing availability flag.

---

## 10. Steam QAM migration

The QAM already has a separate JS/C# adapter because Steam GamepadUI is a React/CEF frontend.

Keep that adapter; do not try to share UI code with WinUI.

### 10.1 QamFrontendBridge

Add one bridge method:

```text
captureDeviceQuickSettings
```

which calls:

```text
NamedPipeAddonFrontendClient.CaptureDeviceQuickSettingsAsync
```

This is only an adapter to the shared typed Runtime contract.

Do not put QAM-only mutation admission rules into the aggregate capture.

### 10.2 qam.js device refresh

Current no-active-game path performs three separate Device feature captures.

Replace that portion with one aggregate request conceptually:

```javascript
const nextDevice = activeGame
  ? null
  : await request("captureDeviceQuickSettings");

const nextCpu = nextDevice?.cpuBoost ?? null;
const nextTdp = nextDevice?.tdp ?? null;
const nextPowerMode = nextDevice?.powerMode ?? null;
```

Use the actual camelCase serialization shape produced by the existing QAM bridge.

Keep separate:

```text
captureStatus
captureActiveGameProfile
```

Do not fold Status or active Game Profile into the Device aggregate in this PR.

Why:

- QAM uses Status for QAM-specific availability policy;
- Profile is a separate product authority/scope;
- the future Overlay has separate Device and Profile tabs;
- making one giant `QuickSettingsEverythingSnapshot` would couple unrelated refresh semantics.

### 10.3 Preserve current QAM draft/invalidation behavior

Do not alter:

- `refreshInFlight` / `refreshDirty` behavior;
- mutation-depth handling;
- deferred invalidation handling;
- 2-second slider commit policy;
- pending TDP/CPU/Power preview preservation;
- fail-closed bridge handling;
- QAM Device mutation admission policy.

This PR changes where the three Device snapshots are read from, not how QAM editing works.

### 10.4 Old QAM JS bridge methods

After `qam.js` has no caller for:

```text
captureCpuBoost
captureTdp
capturePowerMode
```

it is acceptable to remove those **QAM string adapter cases only** if they are genuinely unused.

Do not remove the common typed individual methods/RPCs from `IAddonFrontendControl` / `NamedPipeAddonFrontendClient` / Runtime in this PR.

Avoid compatibility aliases: the product is pre-release.

---

## 11. Future Overlay use — architecture frozen now, implementation deferred

This PR must leave the Overlay code untouched.

The next focused PR should extend the **existing** `.Overlay` transport to consume the new shared DTO.

Conceptually:

```text
Runtime
→ same InProcessAddonFrontendControl
→ CaptureDeviceQuickSettingsAsync
→ FrontendDeviceQuickSettingsSnapshot
→ existing .Overlay endpoint
→ Overlay.exe
```

Do not plan:

```text
.Overlay.Feature
third NamedPipeAddonFrontendServer
full IAddonFrontendControl exposed to Overlay
Overlay opens .Frontend
Overlay opens .Qam
```

OQ-POC-B deliberately reserved the existing `.Overlay` protocol for later narrow Quick Settings feature extension.

The next work order must design the exact v5 → v6 Overlay message/order/refresh semantics against the current OQ4 Show/Visible/capture lifecycle.

Do not pre-implement those messages here.

---

## 12. Future Device features, including Battery Charge Limit

The aggregate should be designed to grow through **typed additive feature contracts**, not a generic settings framework.

Today:

```text
FrontendDeviceQuickSettingsSnapshot
├─ CpuBoost
├─ Tdp
└─ PowerMode
```

A future validated Battery Charge Limit implementation may become conceptually:

```text
FrontendDeviceQuickSettingsSnapshot
├─ CpuBoost
├─ Tdp
├─ PowerMode
└─ BatteryChargeLimit
```

but only after a real Runtime-owned Battery Charge Limit feature exists with its own:

- read contract;
- availability semantics;
- mutation result;
- validation/range policy;
- WMI/ACPI failure policy;
- Center M coexistence policy;
- hardware validation.

`docs/RE_MSI_BatteryChargeLimit.md` currently establishes the low-level MSI WMI path and fail-closed requirements but explicitly does **not** freeze all production UI/range/coexistence policy.

Therefore this PR must not add a placeholder battery field simply to reserve space.

### 12.1 Required future extension rule

When a new Device feature becomes production-ready:

```text
1. implement/freeze one Runtime authority for that feature
2. add a typed Frontend<Feature>Snapshot / mutation result
3. add the typed child to FrontendDeviceQuickSettingsSnapshot
4. update the common frontend protocol version when its wire shape changes
5. update the Overlay protocol version when the .Overlay wire shape changes
6. update each UI presentation independently
```

Do not replace this with:

```text
Dictionary<string, JsonElement>
Dictionary<string, object>
FeatureDescriptor registry
provider/plugin registry
schema-less JSON settings bag
reflection-discovered features
```

The supported feature set is product-controlled and small enough that explicit typed contracts are safer and easier to review.

---

## 13. `StateInvalidated` policy

Keep the existing low-rate invalidation model.

Current common frontend architecture already exposes:

```text
Runtime feature mutation
→ InProcessAddonFrontendControl.StateInvalidated
→ named-pipe StateInvalidated notification
→ frontend refreshes authoritative state
```

Do not introduce in this PR:

```text
CpuBoostChanged
TdpChanged
PowerModeChanged
BatteryLimitChanged
FeatureChanged<T>
EventBus
revision counter
invalidated feature bitmask
```

The aggregate is small. A normal Device refresh can re-read it.

If future measurement proves aggregate refresh too expensive after more hardware-backed features are added, add targeted invalidation as a separate evidence-driven PR.

Do not pre-build it now.

---

## 14. Controller / lifecycle non-interference

This foundation must not change any controller lifecycle path.

No changes to:

- Center M Enabled/Disabled authority decision;
- mandatory Runtime startup policy;
- PID1901/PID1902 transitions;
- DirectInput ownership;
- HidHide deterministic baseline;
- VIIPER runtime/server/bus ownership;
- X360/SteamDeck presentation selection;
- presentation switching;
- owned DirectInput recovery;
- PnP arrival/re-enumeration recovery;
- suspend/hibernate/resume;
- shutdown/restart teardown;
- WING/Game Bar suppression;
- OEM1 behavior;
- OQ4 Overlay controller capture/neutral publication/release gate;
- Main UI ↔ Overlay visible-surface arbitration;
- Steam QAM ↔ Overlay coexistence policy.

A frontend snapshot failure is feature-local. It must never release controller authority or alter presentation ownership.

---

## 15. Expected implementation footprint

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

Expected test changes are focused under:

```text
tests/SteamInputAddonforClaw.Tests/
```

Expected size:

```text
roughly 250–450 LOC changed/added
```

This is guidance, not a correctness limit.

If implementation starts requiring a new project, manager hierarchy, new named pipe, generic RPC abstraction, or hundreds of lines of framework code beyond this footprint, stop and reassess for overengineering.

---

## 16. Required tests

### 16.1 Aggregate contract shape

Verify:

```text
FrontendDeviceQuickSettingsSnapshot
contains typed CpuBoost/Tdp/PowerMode child snapshots
```

and:

```text
FrontendDeviceQuickSettingsSnapshot.Unavailable
→ CpuBoost == FrontendCpuBoostSnapshot.Unavailable
→ Tdp == FrontendTdpSnapshot.Unavailable
→ PowerMode == FrontendPowerModeSnapshot.Unavailable
```

Do not test a generic dynamic feature collection because none should exist.

### 16.2 In-process aggregate capture

Add direct tests proving:

- CPU/TDP/Power Mode values map exactly through their existing frontend semantics;
- absent CPU authority → CPU child unavailable, other children still returned;
- absent TDP authority → TDP child unavailable, other children still returned;
- absent Power Mode authority → Power child unavailable, other children still returned;
- one feature capture failure does not discard otherwise valid sibling snapshots;
- aggregate capture itself causes no persistence mutation;
- aggregate capture does not raise `StateInvalidated` merely because it was read.

Use existing test seams/fakes where possible.

Do not add a new hardware abstraction solely to test the aggregate.

### 16.3 Named-pipe transport

Extend `FrontendNamedPipeTransportTests` or the nearest focused transport tests to prove:

- `CaptureDeviceQuickSettings` round-trips the complete typed aggregate;
- the request has no payload;
- an unexpected payload is rejected as `InvalidMessage` without invoking the frontend operation;
- current protocol is v21;
- old/mismatched protocol still rejects at handshake;
- existing cancellation/disconnect behavior remains unchanged.

Update current-protocol test fixtures that intentionally hard-code v20 so they represent v21 after this PR.

Known current examples include tests/comments that assert:

```text
FrontendTransportProtocol.CurrentVersion == 20
```

and JSON fixtures containing:

```text
"ProtocolVersion":20
```

Only update literals that represent **current protocol**. Do not rewrite historical-version tests whose purpose is to prove older peers are rejected.

### 16.4 Main UI Device page

Add or update the smallest practical tests/source guards proving:

```text
one normal Device refresh
→ CaptureDeviceQuickSettingsAsync once
```

and no longer performs three separate Device capture requests in that refresh path.

Also preserve:

- feature-specific rendering;
- TDP dirty-draft protection;
- unavailable feature disables only its own controls as today;
- transport-level failure does not leave stale editable state.

Do not add full WinUI UI automation solely for trivial wiring if the existing test style uses source/contract tests.

### 16.5 QAM contract

Update `QamFrontendContractTests` and related tests to prove:

- `qam.js` requests `captureDeviceQuickSettings` in Device scope;
- Device-scope refresh no longer independently requests `captureCpuBoost`, `captureTdp`, and `capturePowerMode`;
- active-game/Profile path remains profile-based;
- `captureStatus` remains separate;
- `captureActiveGameProfile` remains separate;
- 2-second delayed slider commit remains;
- pending drafts survive invalidation as before;
- no polling loop is introduced;
- QAM Device mutation admission remains Big Picture/no-running-game scoped exactly as before.

If unused old QAM string adapter cases are removed, test their absence only if that matches the repository's existing source-contract style and improves clarity.

### 16.6 Overlay regression

Even though Overlay production code must not change, run existing Overlay tests to ensure frontend protocol changes did not accidentally disturb shared project references/build output.

Especially keep green:

- Overlay transport tests;
- OQ4 capture/navigation tests;
- tab-order transport tests;
- row/shortcut selection tests.

### 16.7 Full regression

Required before PR completion:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

No new warnings.

---

## 17. Explicit non-goals

Do not implement any of the following in `SHARED-FRONTEND-01`:

### Overlay feature transport

- no `.Overlay` protocol v6;
- no Device snapshot message on `.Overlay`;
- no `StateInvalidated` message on `.Overlay`;
- no Overlay Device-page binding;
- no Overlay TDP/CPU/Power controls.

### New transport architecture

- no `.Overlay.Feature` endpoint;
- no fourth named-pipe namespace;
- no third full `NamedPipeAddonFrontendServer` for Overlay;
- no multi-client `.Frontend`;
- no multi-client `.Qam`;
- no generic message bus;
- no generic RPC framework/reflection dispatcher.

### New state/authority layer

- no `QuickSettingsRuntime`;
- no `QuickSettingsManager`;
- no aggregate cache owner;
- no persisted aggregate snapshot;
- no version/epoch/barrier/state machine;
- no cross-feature synchronization lock.

### New feature implementation

- no Battery Charge Limit implementation;
- no fan control implementation;
- no vibration-strength implementation;
- no new FPS Device/global control;
- no new telemetry.

### Generic future-proofing

- no dynamic feature registry;
- no plugin/provider system;
- no property bag/dictionary settings schema;
- no generic `Setting<T>` framework;
- no arbitrary dashboard/widget model.

### Controller lifecycle

- no PID/HidHide/VIIPER/controller recovery/presentation changes;
- no WING/OEM1/Game Bar/QAM policy changes;
- no OQ4 capture/release changes.

---

## 18. PR completion checklist

Before marking the implementation complete, verify all of the following.

### Architecture

- [ ] one existing `InProcessAddonFrontendControl` remains the common Runtime/frontend projection;
- [ ] no new Device/Quick Settings authority exists;
- [ ] no new hardware reader exists;
- [ ] no new named-pipe endpoint exists;
- [ ] `.Overlay` protocol remains v5 and unchanged;
- [ ] controller/Full1902 lifecycle code is untouched.

### Contract

- [ ] `FrontendDeviceQuickSettingsSnapshot` is typed and contains CPU Boost/TDP/Power Mode child snapshots;
- [ ] `IAddonFrontendControl.CaptureDeviceQuickSettingsAsync` exists;
- [ ] partial feature unavailability is represented per child;
- [ ] aggregate capture is read-only;
- [ ] no generic feature dictionary/registry was introduced.

### Frontend transport

- [ ] `CaptureDeviceQuickSettings` RPC exists;
- [ ] no-payload validation exists;
- [ ] server dispatch exists;
- [ ] typed client method exists;
- [ ] `FrontendTransportProtocol.CurrentVersion` is 21;
- [ ] old/current protocol tests are updated intentionally.

### Main UI

- [ ] Device-page normal refresh performs one aggregate capture;
- [ ] CPU/TDP/Power mutation paths are unchanged;
- [ ] TDP dirty draft behavior is unchanged;
- [ ] partial unavailable state remains feature-local.

### QAM

- [ ] Device-scope refresh uses `captureDeviceQuickSettings`;
- [ ] Status remains separate;
- [ ] active Game Profile remains separate;
- [ ] QAM-specific mutation gating stays in QAM;
- [ ] delayed slider and invalidation behavior is unchanged;
- [ ] no polling is added.

### Validation

- [ ] Debug build passes with 0 warnings / 0 errors;
- [ ] Release build passes with 0 warnings / 0 errors;
- [ ] full Release test suite passes;
- [ ] `git diff --check` is clean.

---

## 19. Next PR boundary

After this PR is merged and proven, the next focused work should be a separate work order, conceptually:

```text
SHARED-FRONTEND-02
Overlay Device Quick Settings Snapshot Transport
```

That PR should:

- reuse `FrontendDeviceQuickSettingsSnapshot` from this PR;
- extend the existing `.Overlay` endpoint only;
- define the exact Show/fresh-snapshot/Visible ordering against current OQ4 capture semantics;
- define low-rate invalidation/refresh while the Overlay is visible;
- keep Overlay lifecycle/navigation writes on the existing serialized wire;
- avoid exposing the whole `IAddonFrontendControl` surface to Overlay;
- avoid a new feature pipe.

Only after that transport foundation should the actual Overlay Device controls be bound in focused feature PRs such as TDP, CPU Boost, and Power Mode.

---

## 20. Review standard

Review this PR for realistic production regressions, especially:

- Main UI/QAM losing valid sibling feature state when one child capture fails;
- stale or incorrectly writable UI after transport failure;
- accidental QAM policy leakage into the shared contract;
- protocol mismatch/serialization errors;
- accidental Overlay/Full1902 lifecycle changes;
- creation of duplicate feature authority/readers.

Do not block the PR for theoretical instruction-level races between adjacent read-only child snapshot captures when the existing feature authorities already synchronize their own state and normal UI refresh converges through `StateInvalidated`.

Do not add locks, epochs, barriers, revision vectors, or generic state machinery solely to make the three child reads transactionally simultaneous.

The design target is:

> **one Runtime feature authority per feature, one explicit typed shared frontend projection, simple disposable consumers, and no duplicate ownership.**
