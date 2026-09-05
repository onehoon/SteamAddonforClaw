# Steam Addon for Claw — Shared Frontend V2 Implementation PR Plan

> **Date:** 2026-09-05  
> **Status:** Current implementation roadmap  
> **Source baseline reviewed:** `main` at `b97c012156b3734ce2230f7e469db91aad94b784`  
> **Architecture authority:** `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md`

---

## 1. Goal

Implement the Shared Frontend architecture in small, reviewable PRs without creating a new frontend authority, a generic settings framework, or a second Overlay feature transport.

The immediate product need is to make the real Addon Quick Settings Overlay consume the same Runtime-owned Device/Profile state already used by the desktop Main UI and Steam QAM.

The implementation sequence must preserve:

```text
Runtime = feature authority
Main UI / QAM / Overlay = disposable presentation clients
```

and:

```text
Frontend typed semantics may be shared
surface exposure / IPC / UI remain explicit and surface-specific
```

---

## 2. Current baseline that drives the split

Current `main` already has:

```text
one InProcessAddonFrontendControl
one .Frontend NamedPipeAddonFrontendServer
one .Qam NamedPipeAddonFrontendServer
one narrow .Overlay transport
Overlay process warm lifecycle
OQ4 controller capture/navigation
five-tab Overlay shell
runtime-owned Overlay tab order
```

Current protocol versions:

```text
FrontendTransportProtocol = 25
OverlayTransportProtocol  = 5
```

Current Device reads are still duplicated across Main UI/QAM:

```text
CaptureCpuBoostAsync
CaptureTdpAsync
CapturePowerModeAsync
```

Overlay currently has no production Device/Profile feature state transport.

Therefore implementation should proceed from shared contracts outward, not from Overlay UI inward.

---

## 3. PR-size / review policy

Keep each PR focused enough that its architectural effect is obvious from the diff.

Preferred target:

```text
roughly 250–500 LOC changed/added per PR when practical
```

This is not a hard correctness limit. Do not split a single invariant across PRs merely to hit an arbitrary line count.

Conversely, if one PR starts adding:

- new projects;
- new manager hierarchies;
- generic RPC abstractions;
- feature registries;
- caches/state stores;
- new pipe endpoints;
- controller lifecycle changes;

stop and reassess for overengineering/scope drift.

---

# Phase A — Shared Device foundation

## 4. SF-V2-01 — Device Quick Settings shared aggregate

### Purpose

Create the one shared typed Device aggregate and migrate the two existing consumers that already need all three child features.

### Production scope

Add:

```text
FrontendDeviceQuickSettingsSnapshot
IAddonFrontendControl.CaptureDeviceQuickSettingsAsync
InProcessAddonFrontendControl aggregate implementation
Frontend RPC CaptureDeviceQuickSettings
NamedPipe client/server support
```

Migrate:

```text
Main UI DevicePage normal refresh
QAM no-active-game Device refresh
```

### Contract

```text
FrontendDeviceQuickSettingsSnapshot
├─ CpuBoost
├─ Tdp
└─ PowerMode
```

Do not add Center M, Status, active Profile, component diagnostics, front-button mapping, or future feature placeholders.

### Failure behavior

Each child remains feature-local:

```text
one child capture failure
→ that child Unavailable
→ healthy siblings still returned
```

Do not add cross-feature locks/epochs/barriers.

### Protocol

Current:

```text
FrontendTransportProtocol = 25
```

This PR changes the wire contract:

```text
25 → 26
```

Do not change `OverlayTransportProtocol`.

### Main UI rules

`DevicePage.RefreshAsync()` becomes one aggregate read for CPU/TDP/Power.

Keep separate:

```text
RefreshCenterMStartupAsync
Device identity/support paths
mutation methods
```

Preserve TDP dirty draft and existing per-feature failure UI.

### QAM rules

Add one explicit bridge operation:

```text
captureDeviceQuickSettings
```

Use it in the no-active-game Device path.

Keep separate:

```text
captureStatus
captureActiveGameProfile
```

Preserve current Big Picture/no-running-game mutation admission in `QamFrontendBridge`.

### Likely production files

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

### Required tests

At minimum:

- aggregate contract/Unavailable shape;
- each child maps existing Runtime semantics exactly;
- one failed/unavailable child does not discard siblings;
- aggregate read causes no persistence/apply side effect;
- named-pipe round trip;
- malformed payload rejection for no-payload method;
- protocol current-version update to 26;
- Main UI normal refresh uses one aggregate capture;
- QAM Device refresh uses `captureDeviceQuickSettings`;
- QAM active-game/Profile behavior unchanged;
- existing Overlay tests remain green.

### Explicit non-goals

- no `.Overlay` changes;
- no Overlay UI changes;
- no Center M changes;
- no controller lifecycle changes;
- no new cache/manager;
- no deletion of focused CPU/TDP/Power capture methods.

---

# Phase B — Overlay Device transport

## 5. SF-V2-02 — Overlay Device Quick Settings transport

### Purpose

Extend the existing narrow `.Overlay` protocol so the real Overlay process can consume and mutate the same Device Quick Settings semantics without gaining access to the full desktop/QAM frontend API.

### Required reading before implementation

Re-read current implementations of:

```text
OverlayWire.cs
OverlayProcessController.cs
Overlay App.xaml.cs
OverlayWindow.xaml.cs
AddonProcessHost.cs
OQ4 capture/navigation tests
OQ5 tab-order transport tests
```

Do not implement from older OQ planning snippets alone.

### Transport direction

```text
same Runtime-owned CPU/TDP/Power authorities
        ↓
InProcessAddonFrontendControl
        ↓
FrontendDeviceQuickSettingsSnapshot + existing mutation semantics
        ↓
small explicit Runtime/Overlay binding
        ↓
existing .Overlay endpoint
        ↓
Overlay.exe
```

### Wire scope

Add only explicit Device feature messages required by approved Overlay controls.

The transport must support:

```text
fresh Device Quick Settings state delivery
visible-session invalidation refresh
approved CPU Boost mutations
approved TDP mutations
approved Power Mode mutations
```

Reuse the shared typed frontend snapshot/result records where their semantics already fit.

Do not create duplicate Overlay CPU/TDP/Power DTOs.

### Exact request/result shape

Choose the smallest explicit shape that fits current `OverlayWire` architecture.

Acceptable direction:

```text
DeviceQuickSettingsState
explicit Device mutation request kinds
explicit mutation result/state refresh
```

Do not redesign `.Overlay` into a generic RPC framework just to gain request IDs or generic dispatch.

If mutation correlation is needed for actual current UI behavior, add only the minimum concrete correlation required; do not add speculative sequence/epoch machinery.

### Initial state/invalidation behavior

Required semantics:

```text
Overlay Ready
→ Runtime can send authoritative Device state

Overlay Show
→ current OQ4 capture/show ordering remains authoritative
→ fresh Device state sent for the visible session

StateInvalidated while Overlay visible
→ fresh Device aggregate sent

Overlay hidden
→ no polling loop
```

A Device snapshot failure must not prevent Overlay Show/Hide or weaken OQ4 controller capture safety.

### Runtime binding

Use the existing `_frontendControl` as the source of truth.

Bind the Overlay controller/server with explicit delegates or the smallest adjacent seam.

Do not create:

```text
OverlayFeatureManager
OverlayDeviceRuntime
QuickSettingsService
third NamedPipeAddonFrontendServer
.Overlay.Feature pipe
```

### Protocol

Current:

```text
OverlayTransportProtocol = 5
```

This PR changes the wire:

```text
5 → 6
```

`FrontendTransportProtocol` remains 26 from SF-V2-01 unless another actual desktop/QAM wire change is required.

### UI scope

Do **not** bind the production Device controls yet beyond the minimum state receiver/store needed for transport tests.

The Overlay may keep placeholder/sample rows until the following UI-binding PRs.

### Required tests

At minimum:

- v6 handshake / v5 rejection;
- Device state round trip;
- partial child unavailability survives serialization;
- approved mutation request reaches the same Runtime/frontend operation;
- mutation failure does not close Overlay or alter controller capture;
- `StateInvalidated` refresh only delivers while appropriate;
- no feature polling loop;
- show/hide/navigation/tab-order frames still serialize correctly through the existing write gate;
- OQ4 visible-session-loss/capture tests remain green;
- Overlay process disconnect does not affect Runtime feature/controller ownership.

### Explicit non-goals

- no new pipe;
- no full `IAddonFrontendControl` exposure;
- no Profile transport;
- no Controller-feature transport;
- no UI redesign;
- no Full1902 lifecycle change.

---

# Phase C — Overlay Device UI binding

## 6. SF-V2-03 — Overlay CPU Boost + Power Mode binding

### Purpose

Replace placeholder Device rows for the lower-complexity Device features with real Runtime-backed controls.

Group CPU Boost and Windows Power Mode because they share similar toggle/selection interaction and should fit comfortably in one focused PR without touching TDP slider commit behavior.

### Runtime authority

Use only:

```text
FrontendDeviceQuickSettingsSnapshot.CpuBoost
FrontendDeviceQuickSettingsSnapshot.PowerMode
existing typed mutation results
```

No direct Windows power APIs or ProfileStore access from Overlay.

### UI behavior

- authoritative initial render from Device snapshot;
- disabled/unavailable rendering per child;
- toggle/selection writes through `.Overlay` v6 explicit mutation messages;
- returned Runtime snapshot/result wins;
- transport failure disables speculative mutation and surfaces a compact failure state;
- no local persistence;
- controller navigation uses existing Overlay row/control primitives.

### Preserve

- B global close;
- LB/RB tab navigation;
- OQ4 controller capture/neutral publication;
- current show/hide animation/lifecycle;
- current internal inset/layout authority;
- five-tab shell/tab order.

### Likely files

Primarily:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
existing Overlay row/control helpers
possibly minimal Overlay App/client state wiring
focused tests
```

Do not create a ViewModel framework solely for these controls.

### Protocol

No protocol bump if SF-V2-02 already froze the required Device messages.

If implementation discovers a real missing wire operation, stop and update the transport contract deliberately rather than silently adding UI-local workarounds.

---

## 7. SF-V2-04 — Overlay TDP binding and commit behavior

### Purpose

Bind TDP separately because it has materially different slider/draft/commit semantics and deserves focused validation.

### Runtime authority

Use:

```text
FrontendDeviceQuickSettingsSnapshot.Tdp
FrontendTdpMutationResult
```

No direct TDP helper/hardware access from Overlay.

### Required interaction policy

Preserve the Overlay/QAM-style relaxed slider behavior already chosen for Quick Settings:

```text
Left/Right changes local visible draft immediately
→ debounce/commit according to current agreed Overlay control policy
→ Runtime mutation
→ authoritative result/snapshot applied
```

Do not introduce an A-to-enter-edit-mode unless product UI design changes explicitly.

### Draft safety

A normal invalidation must not destroy an in-progress local slider gesture/draft in a way that makes the control jump backward during ordinary use.

Solve this with the smallest existing local UI draft/suppression pattern.

Do not create global versions/epochs/transactions to defend theoretical interleavings.

### Failure behavior

- invalid/unavailable TDP disables mutation;
- persistence/hardware failure surfaces concise feedback;
- Runtime result wins after commit;
- Overlay/controller lifecycle remains unaffected.

### Required tests

- range/step uses Runtime-provided limits;
- AC/DC PL1/PL2 values render correctly;
- toggle state renders correctly;
- draft survives ordinary invalidation while editing;
- commit reaches one explicit Runtime mutation path;
- failed mutation does not leave a false committed value;
- navigation and B-close remain correct while a debounce is pending;
- close cancels/discards only transient UI work, not Runtime authority.

---

# Phase D — Profile reuse

## 8. SF-V2-05 — Overlay Profile transport using existing profile contracts

### Purpose

Add Overlay access to current active-game profile state without inventing a second Profile model.

### Reuse

Use existing:

```text
FrontendGameProfileSnapshot
FrontendGameProfileMutationResult
```

and existing Runtime profile mutation methods.

Do not add:

```text
FrontendOverlayProfileSnapshot
SharedProfileSnapshot
OverlayProfileStore
```

### Exposure

Only approved compact Profile controls should cross `.Overlay`.

The current profile contract contains more capability than Overlay may ultimately present; transport exposure remains explicit.

### Active-game behavior

Overlay Profile must derive state from Runtime's active-game/profile authority, not from a second process scan or Steam probe inside Overlay.

If no active supported profile exists:

```text
show deliberate unavailable/empty Profile state
```

Do not silently fall back to Device settings while still labeling the page Profile.

### Wire/version choice

Because this PR changes `.Overlay` wire after v6, expect:

```text
OverlayTransportProtocol 6 → 7
```

unless the full Profile wire was intentionally and explicitly frozen earlier. Do not pre-add unused Profile messages in SF-V2-02 merely to avoid a future version bump.

### UI scope

Prefer transport/state foundation first. If full Profile UI binding would push the diff too large or mix transport and interaction bugs, leave visible binding to SF-V2-06.

---

## 9. SF-V2-06 — Overlay Profile UI binding

### Purpose

Bind the approved active-game Profile controls to the v7 Profile transport.

Likely reusable feature semantics:

```text
TDP
Intel FPS Limit
CPU Boost
Windows Power Mode
Resolution
```

Do not assume every existing Main UI Profile control must appear in Overlay. The exact visible subset must be explicitly frozen in the focused work order before implementation.

### Requirements

- Runtime snapshot is authoritative;
- no profile persistence in Overlay;
- local slider/dropdown draft only as needed for UX;
- mutation results applied from Runtime;
- no active game = clear unavailable state;
- Device/Profile scopes never silently alias each other;
- no new game detector in Overlay.

---

# Phase E — Controller and future Device features

## 10. Do not create a Controller shared-foundation PR yet

Current/future Controller features are not mature enough to justify a speculative aggregate.

Examples:

```text
front-button mapping
M1/M2 mapping
Joystick LED
Vibration Strength
```

Proceed feature-by-feature when real Runtime contracts and product surface decisions exist.

For each future feature work order, explicitly state:

```text
Supported frontend surfaces
Main UI: Yes/No
Steam QAM: Yes/No
Addon Overlay: Yes/No
```

Then reuse existing typed contracts or add the smallest typed feature contract needed.

Do not create a generic `FrontendControllerQuickSettingsSnapshot` now simply because an Overlay Controller tab exists.

---

## 11. Future Device features follow the same rule

Examples:

```text
Fan Control
Battery Charge Limit
```

A future Runtime implementation does not automatically enter `FrontendDeviceQuickSettingsSnapshot`.

Decision sequence:

```text
1. real Runtime feature implemented and validated
2. product decides supported frontend surfaces
3. if shared Quick Settings exposure is approved, add the smallest typed shared projection
4. update only the transports/surfaces that actually expose it
```

No placeholder members.

---

# Cross-PR invariants

## 12. Full1902 lifecycle must remain untouched

None of SF-V2-01 through SF-V2-06 may change, except for purely mechanical compile wiring that does not alter behavior:

```text
Center M authority policy
PID1901/PID1902 transitions
DirectInput ownership
HidHide baseline
VIIPER ownership
presentation selection
physical-device recovery
suspend/resume
restart/shutdown teardown
Win+G suppression
stock restoration
OQ4 capture safety
```

If a Shared Frontend PR appears to require changing any of the above, stop and re-evaluate the design.

---

## 13. No new authority/caching layer

Across the whole track, do not add:

```text
SharedFrontendManager
QuickSettingsManager
DeviceSettingsManager
OverlayFeatureManager
FrontendStateCache
FeatureRegistry
SurfaceRegistry
EventBus
```

The intended owners already exist.

A frontend can hold transient presentation state/drafts while visible, but it is never persistence or hardware authority.

---

## 14. Transport boundaries stay intentionally different

After the Device foundation:

```text
.Frontend
→ explicit full frontend RPC surface needed by desktop UI

.Qam
→ same typed frontend RPC transport
→ QamFrontendBridge explicit JS allowlist

.Overlay
→ dedicated narrow lifecycle/navigation/approved-feature protocol
```

Do not unify them merely to reduce duplicate switch cases.

Some explicit dispatch duplication is desirable because it preserves the product allowlist.

---

## 15. State refresh remains event-driven

Normal feature UI should use:

```text
authoritative initial capture
+
StateInvalidated-driven re-read
+
mutation-result readback
```

Do not add high-frequency polling to Main UI, QAM, or Overlay as part of Shared Frontend work.

Diagnostic sensor/probe polling is a separate developer-feature concern and does not justify a generic frontend polling architecture.

---

## 16. Failure handling standard

Every PR should preserve this hierarchy:

```text
feature read/mutation failure
→ feature-local UI failure

frontend/Overlay transport failure
→ surface fails closed
→ Runtime survives

Overlay UI/process failure
→ OQ4/session-loss cleanup handles capture
→ controller Runtime survives

controller/hardware/lifecycle operation failure
→ existing Full1902 fail-close policy
```

Do not escalate a CPU/TDP/Power UI error into controller ownership teardown.

---

# Verification strategy

## 17. Per-PR verification

Every implementation PR should run at minimum:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

No new warnings.

Use focused tests during development, then full regression before completion.

---

## 18. Hardware validation points

### SF-V2-01

No new hardware behavior should be introduced; code-level regression is primary. Existing Device/QAM behavior should remain functionally identical.

### SF-V2-02

Validate on real MSI Claw:

- Overlay still opens/closes reliably;
- controller navigation capture remains safe;
- Device state arriving/failing does not change presentation;
- hidden Overlay causes no measurable polling activity.

### SF-V2-03 / 04

Validate actual Device mutations from Overlay:

- CPU Boost AC/DC + Enabled;
- Power Mode AC/DC + Enabled;
- TDP Enabled + AC/DC PL1/PL2;
- state agrees with Main UI/QAM after mutation;
- reopen Overlay shows Runtime truth;
- game/controller input does not leak through while Overlay capture is active.

### Profile PRs

Validate an actual running Steam game/profile plus no-active-game state.

---

# Dependency graph

## 19. Recommended sequence

```text
SF-V2-01
Device shared aggregate
Main UI + QAM migration
Frontend protocol 25 → 26
        │
        ▼
SF-V2-02
Overlay Device feature transport
Overlay protocol 5 → 6
        │
        ├───────────────┐
        ▼               ▼
SF-V2-03           SF-V2-04
CPU + Power UI     TDP UI
        │               │
        └───────┬───────┘
                ▼
        Device Overlay complete
                │
                ▼
SF-V2-05
Overlay Profile transport
Overlay protocol 6 → 7
                │
                ▼
SF-V2-06
Overlay Profile UI binding
```

SF-V2-03 and SF-V2-04 can be implemented sequentially in either order after SF-V2-02. Do not run them concurrently if they would heavily modify the same Overlay page construction code and create avoidable merge churn.

Controller/future Device feature work starts only when each real feature contract is ready.

---

## 20. Why this split is preferred

This sequence avoids two bad extremes.

### Bad extreme A — giant frontend rewrite

```text
new generic shared frontend framework
+ all Main UI migration
+ QAM migration
+ Overlay transport
+ Device UI
+ Profile UI
+ Controller framework
```

Too much scope, weak review boundaries, and high risk of accidental authority changes.

### Bad extreme B — one transport/DTO per control

```text
OverlayCpuBoostPipe
OverlayTdpDto
OverlayPowerState
separate mini managers
```

Creates duplicate semantics and permanent fragmentation.

The proposed split instead freezes reusable typed truth first, then adds one narrow surface transport, then binds UI by interaction complexity.

---

# Completion criteria

## 21. Shared Frontend V2 Device milestone complete

The Device milestone is complete after SF-V2-01 through SF-V2-04 when all of the following are true:

```text
one Runtime CPU authority
one Runtime TDP authority
one Runtime Power Mode authority
        ↓
one FrontendDeviceQuickSettingsSnapshot
        ↓
Main UI + QAM + Overlay consume same typed state
        ↓
all mutations return to same Runtime-owned feature methods
```

and:

- no duplicate state cache exists;
- no new hardware reader exists;
- no new named-pipe endpoint exists;
- QAM admission remains QAM-specific;
- Overlay remains on its narrow protocol;
- controller/Full1902 lifecycle remains untouched;
- real-device Overlay mutations are validated.

---

## 22. Shared Frontend V2 Profile milestone complete

After SF-V2-05/06:

- Overlay reuses existing `FrontendGameProfileSnapshot` / mutation semantics;
- no second Profile model/store exists;
- no-active-game state is explicit;
- Overlay does not scan games independently;
- Device and Profile scopes remain distinct;
- only approved compact Profile features are exposed.

---

## 23. Review policy

Review for realistic production defects:

- duplicate authority/readers;
- protocol mismatch or serialization failure;
- stale editable state after disconnect;
- feature-local failure becoming aggregate/surface-wide failure;
- incorrect Main UI/QAM/Overlay state agreement;
- accidental surface widening;
- Overlay feature traffic disturbing OQ4 capture/navigation/close safety;
- real lifecycle/resource leaks.

Do not require locks, epochs, barriers, registries, or generalized managers solely to defend theoretical instruction-level races when existing Runtime owners and event-driven refresh converge safely in normal supported lifecycle.

---

## 24. Next action

Prepare the focused implementation work order for:

```text
SF-V2-01 — Device Quick Settings shared aggregate
```

against the latest `main` immediately before coding.

Do not reuse the old `SHARED_FRONTEND_01_DEVICE_QUICK_SETTINGS_SNAPSHOT_WORK_ORDER.md` verbatim; use it only as historical rationale together with `SHARED_FRONTEND_ARCHITECTURE_V2.md` and the current source.