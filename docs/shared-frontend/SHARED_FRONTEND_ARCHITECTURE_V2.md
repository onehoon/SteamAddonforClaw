# Steam Addon for Claw — Shared Frontend Architecture V2

> **Date:** 2026-09-05  
> **Status:** Current design authority for shared Runtime → frontend feature projection  
> **Baseline reviewed:** `main` at `b97c012156b3734ce2230f7e469db91aad94b784`  
> **Scope:** Addon Runtime, desktop Main UI, Steam QAM, Addon Quick Settings Overlay, typed frontend contracts, and surface-specific exposure boundaries

---

## 1. Purpose

The Addon now has three materially different user-facing frontend surfaces:

```text
SteamInputAddonforClaw.UI.exe
→ desktop WinUI management/configuration UI

SteamInputAddonforClaw.QamHost.exe
→ Steam GamepadUI / CEF QAM integration

SteamInputAddonforClaw.Overlay.exe
→ Addon-owned native WinUI Quick Settings overlay
```

All three need access to some of the same Runtime-owned feature state.

The architecture must therefore share **truth and typed semantics** without creating duplicate feature owners, duplicate hardware readers, duplicate persisted state, or a generic cross-surface UI framework.

The target is:

```text
one Runtime authority per real feature
        ↓
one typed frontend projection where sharing is useful
        ↓
explicit per-surface transport/allowlist
        ↓
surface-specific UI and interaction policy
```

This document replaces the planning assumptions in `SHARED_FRONTEND_01_DEVICE_QUICK_SETTINGS_SNAPSHOT_WORK_ORDER.md` when they conflict with current `main`. The older work order remains useful as historical design rationale.

`SHARED_FRONTEND_01_SURFACE_EXPOSURE_POLICY_ADDENDUM.md` remains conceptually valid and is incorporated here as a core rule.

---

## 2. Required authority documents

Read this document together with the current Full1902 authority set, in its documented precedence order:

1. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
2. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
3. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
4. current `docs/work-order/` implementation work orders/addenda

Also read the current frontend/UI design documents:

- `docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md`
- `docs/overlayui/README.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/shared-frontend/SHARED_FRONTEND_01_SURFACE_EXPOSURE_POLICY_ADDENDUM.md`

When an older Overlay document says the Overlay process/transport is still design-only, current source and newer OQ work orders take precedence: the warm Overlay process, `.Overlay` transport, controller capture/navigation, five-tab shell, and tab-order transport now exist on `main`.

---

## 3. Frozen product/lifecycle invariants

Shared frontend work is presentation/data projection work. It must not become controller authority.

The following remain Runtime-owned and outside frontend authority:

```text
Center M Enabled/Disabled authority
PID1901 / PID1902 ownership
DirectInput physical ownership
HidHide deterministic baseline
VIIPER native/runtime ownership
X360 / SteamDeck presentation ownership
physical-device loss / PnP recovery
sleep / hibernate / resume handling
restart / shutdown teardown
Win+G suppression ownership
Full1902 fail-close and stock-restoration paths
```

The Addon Overlay is a transient UI surface. Main UI and QAM are also disposable clients of Runtime truth.

A frontend crash, disconnect, stale page, failed render, or failed feature capture must not change controller authority or trigger controller teardown.

---

## 4. Current implementation facts on `main`

### 4.1 One common Runtime/frontend projection object already exists

`AddonProcessHost` constructs one production `InProcessAddonFrontendControl` and stores it as `_frontendControl`.

The same object projects existing Runtime-owned capabilities including, among others:

```text
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
GameProfileMutations
IntelFrameLimiterRuntime
CenterMStartupControl
front-button settings
```

This is already the correct common Runtime/frontend projection boundary.

Do not add:

```text
QuickSettingsRuntime
SharedFrontendRuntime
DeviceSettingsManager
OverlayFeatureManager
QamFeatureManager
SharedDeviceStateCache
FrontendAuthorityManager
```

### 4.2 Main UI and QAM already share the same Runtime authority

Current composition is:

```text
same _frontendControl
      │
      ├─ .Frontend → NamedPipeAddonFrontendServer → Main UI
      │
      └─ .Qam      → NamedPipeAddonFrontendServer → QamHost
```

The two pipe endpoints have different process lifetimes, but they do not represent different feature authorities.

Current common frontend transport protocol is:

```text
FrontendTransportProtocol.CurrentVersion = 25
```

### 4.3 Overlay intentionally uses a different transport

Current Overlay composition is conceptually:

```text
Runtime / OverlayProcessController
        ↓
existing .Overlay named pipe
        ↓
NamedPipeOverlayServer / NamedPipeOverlayClient
        ↓
SteamInputAddonforClaw.Overlay.exe
```

Current Overlay protocol is:

```text
OverlayTransportProtocol.CurrentVersion = 5
```

It currently carries lifecycle/navigation/preference traffic such as:

```text
Handshake / Ready
Show / Hide / Shutdown
Visible / Hidden
semantic Navigation
DismissRequested
TabOrderState
SetTabOrder
```

This narrow protocol exists for real Overlay lifecycle and OQ4 input-capture reasons.

Do **not** replace it with a third full `NamedPipeAddonFrontendServer`.

Do **not** make Overlay connect to `.Frontend` or `.Qam`.

### 4.4 The Device shared-read problem still exists

Current Main UI `DevicePage.RefreshAsync()` still performs separate reads for:

```text
CaptureCpuBoostAsync
CaptureTdpAsync
CapturePowerModeAsync
```

Current QAM device-scope refresh likewise requests the three Device features separately through `QamFrontendBridge` / `qam.js`.

The original reason for a typed Device aggregate therefore still exists.

---

## 5. Core architecture rule: share semantics, not surfaces

The term **Shared Frontend** means:

```text
shared Runtime truth
+
shared typed feature projection where appropriate
```

It does **not** mean:

```text
shared XAML/React UI
shared navigation model
shared visibility policy
one common pipe for every frontend
one giant settings snapshot
all features visible on all surfaces
```

Keep these four concerns separate:

```text
1. Runtime authority
2. typed frontend data/mutation semantics
3. surface exposure + transport
4. surface-specific presentation/interaction
```

This separation is required to avoid accidental authority duplication and accidental feature exposure.

---

## 6. Surface exposure is an explicit product allowlist

A feature existing in Runtime does not imply that every frontend may expose it.

For every feature, decide explicitly:

```text
Main UI: Yes / No
Steam QAM: Yes / No
Addon Overlay: Yes / No
```

Examples of valid product shapes:

```text
Main UI only
Main UI + QAM
Main UI + Overlay
Main UI + QAM + Overlay
```

Do not infer exposure from:

- which Main UI page contains the feature;
- whether the feature is global/device-scoped;
- whether a typed frontend contract exists;
- whether another surface already exposes a similar control;
- whether transport support would be easy to add.

Do not build a second authority such as:

```text
FeatureSurfaceMatrix
FrontendCapabilityRegistry
FeatureVisibilityRegistry
SurfacePolicyManager
VisibleInMainUi / VisibleInQam / VisibleInOverlay metadata
```

The supported surface set is small. Explicit work orders and explicit dispatch/allowlists are preferred.

---

## 7. Domain-oriented typed projections

Do not create a single `QuickSettingsEverythingSnapshot` or `AppFrontendState`.

Use small typed projections around real product domains.

### 7.1 Device Quick Settings — first shared aggregate

The first shared aggregate remains:

```text
FrontendDeviceQuickSettingsSnapshot
├─ FrontendCpuBoostSnapshot CpuBoost
├─ FrontendTdpSnapshot Tdp
└─ FrontendPowerModeSnapshot PowerMode
```

Recommended contract shape:

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

These three features are a good first aggregate because:

- all three are Runtime-owned today;
- Main UI already reads all three together on the Device page;
- QAM already reads all three together in Device scope;
- Overlay Device is intended to expose compact runtime controls for the same class of settings;
- their feature-specific typed contracts already exist.

### 7.2 Do not put Center M authority into this aggregate

MSI Center M authority lives on the Device page, but page placement does not make it an ordinary quick setting.

Center M authority is:

```text
reboot-bound
controller-authority changing
Full1902 lifecycle critical
separately refreshed in current DevicePage
not currently an approved QAM/Overlay quick mutation
```

Therefore keep:

```text
CaptureCenterMStartupAsync
RequestCenterMAuthorityTransitionAsync
```

outside `FrontendDeviceQuickSettingsSnapshot`.

Do not expose the reboot-bound authority transition through QAM or Overlay without a separate explicit product work order.

### 7.3 Profile already has the shared typed contract it needs

Current source already contains:

```text
FrontendGameProfileSnapshot
FrontendGameProfileMutationResult
```

including typed CPU Boost, TDP, Power Mode, FPS limit, resolution, enabled state, and persistence semantics.

Do not create:

```text
SharedProfileSnapshot
OverlayProfileSnapshot
QamProfileSnapshot
```

merely to give Overlay access later.

Overlay Profile work should reuse `FrontendGameProfileSnapshot` and existing typed mutation results, while exposing only approved operations on the `.Overlay` wire.

### 7.4 Controller grows feature-by-feature

Current/future Controller features include:

```text
front-button mapping
M1/M2 mapping
Joystick LED
Vibration Strength
```

Do not create a generic `FrontendControllerQuickSettingsSnapshot` before there is a concrete group of Runtime-owned controller features that multiple surfaces genuinely consume together.

Existing typed settings contracts such as `FrontButtonMappingSettings` should be reused where appropriate rather than copied into Overlay-specific DTOs.

### 7.5 Settings and diagnostics are not automatically shared

Developer probes, environment reports, component diagnostics, setup/recovery operations, and other Main-UI-oriented tools do not belong in shared Quick Settings merely because `IAddonFrontendControl` exposes them.

The desktop Main UI remains the superset-capable management surface.

---

## 8. `IAddonFrontendControl` remains the common projection boundary

Add one aggregate read seam conceptually:

```csharp
Task<FrontendDeviceQuickSettingsSnapshot> CaptureDeviceQuickSettingsAsync(
    CancellationToken cancellationToken = default);
```

Keep the existing focused methods:

```text
CaptureCpuBoostAsync
CaptureTdpAsync
CapturePowerModeAsync
```

and existing feature-specific mutation methods.

Reasons:

- mutation results remain feature-specific;
- focused reads remain useful to focused callers/tests;
- deleting them would create unrelated churn;
- the aggregate is a convenience/projection seam, not a replacement feature framework.

Do not change `IAddonFrontendControl` into a generic method bag such as:

```text
GetFeature(string name)
InvokeSetting(...)
Dictionary<string, JsonElement>
FeatureDescriptor[]
```

---

## 9. Aggregate capture semantics

`CaptureDeviceQuickSettingsAsync` must aggregate the existing Runtime authorities only.

Conceptually:

```text
CpuBoostRuntime ───────────────┐
TdpRuntime ────────────────────┼─→ existing mapping semantics
PowerModeRuntime ──────────────┘
                                  ↓
                   FrontendDeviceQuickSettingsSnapshot
```

It must not:

- create new hardware readers;
- create a cache;
- persist anything;
- reconcile/apply merely because state was read;
- trigger controller/presentation changes;
- create an aggregate transaction.

### 9.1 Feature-local failure isolation

A failure in one child capture must not erase valid sibling state.

Desired behavior:

```text
CPU capture fails
→ CpuBoost = Unavailable
→ TDP + Power Mode still returned

TDP capture fails
→ Tdp = Unavailable
→ CPU + Power Mode still returned

Power capture fails
→ PowerMode = Unavailable
→ CPU + TDP still returned
```

Cancellation/process shutdown may still terminate the aggregate request through existing lifecycle semantics.

### 9.2 No cross-feature synchronization machinery

The aggregate is a UI projection, not a hardware transaction.

Do not add:

```text
QuickSettingsSnapshotLock
epoch
revision vector
barrier
transaction coordinator
atomic multi-feature read
```

Adjacent Runtime snapshots are sufficient for a Quick Settings UI. Existing feature authorities remain responsible for their own synchronization.

---

## 10. Main UI transport and consumption

The desktop Main UI should consume the shared Device aggregate through the existing `.Frontend` transport.

After the foundation change:

```text
DevicePage.RefreshAsync
        ↓
CaptureDeviceQuickSettingsAsync
        ↓
Render CpuBoost
Render Tdp
Render PowerMode
```

Keep separate:

```text
Center M authority refresh
Device identity/support information
other Device-page-specific operations
```

Preserve current UI-specific behaviors including:

- TDP dirty draft preservation;
- mutation-result authoritative readback;
- per-feature unavailable/error rendering;
- `StateInvalidated` refresh behavior.

The Main UI must never become a state authority/cache.

---

## 11. Steam QAM transport and consumption

QAM continues to use:

```text
.Qam
→ NamedPipeAddonFrontendServer
→ same IAddonFrontendControl
→ QamFrontendBridge explicit JS allowlist
```

The QAM no-active-game Device refresh should move from three individual requests to one aggregate request.

Keep separate:

```text
captureStatus
captureActiveGameProfile
```

because Status/QAM eligibility and active Profile are different scopes.

### 11.1 QAM-specific admission remains QAM-specific

Current QAM Device mutation policy checks Big Picture / no-running-game conditions in `QamFrontendBridge`.

That is surface policy.

Do not move it into:

- `IAddonFrontendControl`;
- `FrontendDeviceQuickSettingsSnapshot`;
- `CpuBoostRuntime`;
- `TdpRuntime`;
- `PowerModeRuntime`.

Main UI and Overlay must not inherit QAM-only admission rules simply because they share typed state.

---

## 12. Overlay transport architecture

Overlay must consume shared typed semantics without consuming the full desktop/QAM RPC surface.

Target direction:

```text
Runtime feature authorities
        ↓
InProcessAddonFrontendControl
        ↓
shared typed snapshot / mutation semantics
        ↓
small explicit binding at Runtime/Overlay boundary
        ↓
existing .Overlay pipe
        ↓
Overlay.exe
```

### 12.1 Keep `.Overlay` narrow

Do not add:

```text
.Overlay.Feature
another named pipe
third full NamedPipeAddonFrontendServer
Overlay connects to .Frontend
Overlay connects to .Qam
reflection/generic frontend passthrough
```

The existing `.Overlay` endpoint should be extended with only the exact approved Device Quick Settings messages.

### 12.2 Reuse typed frontend contracts on the wire where useful

Do not create duplicate shapes such as:

```text
OverlayCpuBoostSnapshot
OverlayTdpSnapshot
OverlayPowerModeSnapshot
```

The transport may carry the existing typed frontend snapshot/result records directly when their semantics are exactly what Overlay needs.

Overlay-specific protocol messages should describe **wire intent** such as state delivery or a specific mutation request, not duplicate the feature data model.

### 12.3 Bind to the existing authority with the smallest seam

`OverlayProcessController` / `NamedPipeOverlayServer` may receive narrow capture/mutation delegates or the smallest adjacent explicit access seam needed to call the existing `_frontendControl`.

Do not introduce a new long-lived manager/interface hierarchy solely to route three features.

The exact code shape should be chosen in the focused Overlay transport work order after reviewing current `OverlayProcessController` and `OverlayWire` implementation.

---

## 13. Overlay state delivery and invalidation

Use event-driven, low-rate authoritative refresh.

Do not add feature polling merely because Overlay is visible.

Preferred behavior:

```text
Overlay connection Ready
→ Runtime can provide current Device snapshot

Overlay Show
→ current OQ4 lifecycle/capture ordering remains authoritative
→ Runtime sends/re-sends a fresh Device snapshot for this visible session

Runtime feature mutation / StateInvalidated
→ while Overlay is visible, send a fresh Device snapshot

Overlay hidden
→ no periodic feature refresh
```

The exact placement of the first state message relative to Show/Visible acknowledgement must preserve current OQ4 capture safety and must not make controller neutralization depend on Device capture success.

Feature snapshot failure is feature-local:

```text
snapshot unavailable
→ Overlay disables/marks affected controls unavailable
→ Overlay may still open/close safely
→ controller capture/lifecycle remains correct
```

Do not make Overlay visibility fail merely because TDP/CPU/Power state could not be read.

---

## 14. Overlay mutation semantics

For approved Device controls, Overlay mutations must call the same Runtime-owned feature methods already used by Main UI/QAM.

Conceptually:

```text
Overlay user action
→ explicit .Overlay mutation message
→ existing Runtime/frontend mutation method
→ typed mutation result / authoritative snapshot
→ Overlay render
```

No Overlay-side speculative state may become authoritative.

For sliders/toggles:

- local transient preview/draft is allowed for interaction quality;
- commit must use Runtime mutation methods;
- returned Runtime state wins;
- transport failure must fail closed in the UI and re-read when possible;
- no new persisted Overlay copy of Device settings.

Do not add a generic `InvokeFeature` message.

---

## 15. Protocol version policy from current baseline

Current baseline:

```text
FrontendTransportProtocol = 25
OverlayTransportProtocol  = 5
```

When `CaptureDeviceQuickSettings` is added to the desktop/QAM frontend protocol:

```text
FrontendTransportProtocol 25 → 26
```

Do not bump Overlay solely for that change.

When `.Overlay` gains real Device feature state/mutation messages:

```text
OverlayTransportProtocol 5 → 6
```

If later PRs change the Overlay wire again, bump again. The product is pre-release; prefer honest handshake mismatch over compatibility aliases.

Do not renumber historical protocol comments/tests.

---

## 16. Main UI / QAM / Overlay presentation remains independent

Shared typed contracts do not imply shared UI code.

Correct model:

```text
same FrontendCpuBoostSnapshot
        ↓
Main UI → WinUI Expander / ComboBox / Toggle
QAM     → JS/React-like Steam UI adapter
Overlay → compact controller-navigable WinUI row/control
```

Each surface may have different:

- layout;
- labels;
- navigation;
- busy indicators;
- draft behavior;
- mutation admission;
- visibility policy.

Do not create a cross-framework UI component abstraction.

---

## 17. Current product placement and likely sharing

Current Main UI information architecture is:

```text
Device
Controller
Profile
How to Use
Settings
```

Current Overlay tabs are:

```text
Device
Profile
Controller
Shortcut
Setting
```

This does not mean the tabs must expose identical content.

Current initial sharing decision:

| Feature/domain | Main UI | QAM | Overlay | Shared-contract direction |
|---|---:|---:|---:|---|
| CPU Boost | Yes | Yes | Yes | `FrontendDeviceQuickSettingsSnapshot` child |
| TDP | Yes | Yes | Yes | `FrontendDeviceQuickSettingsSnapshot` child |
| Windows Power Mode | Yes | Yes | Yes | `FrontendDeviceQuickSettingsSnapshot` child |
| Active game Profile | Yes | Yes | Planned | reuse existing `FrontendGameProfileSnapshot` |
| Center M authority | Yes | No | No | keep separate / Main UI only for now |
| Front-button mapping | Yes | product-specific | product-specific | reuse existing typed mapping contract when approved |
| Component diagnostics | Settings/Main UI | No | No | no shared Quick Settings aggregate |
| Developer diagnostics | Main UI | No | No | no shared Quick Settings aggregate |
| Battery Charge Limit | future | undecided | undecided | no placeholder; decide surface scope with feature work order |
| Fan Control | future | undecided | undecided | no placeholder; decide surface scope with feature work order |
| Joystick LED | future | undecided | undecided | no placeholder; decide surface scope with feature work order |
| Vibration Strength | future | undecided | undecided | no placeholder; decide surface scope with feature work order |

Future feature work must explicitly state supported surfaces rather than infer them from this table.

---

## 18. State invalidation policy

Keep the existing low-rate invalidation model where practical:

```text
Runtime mutation/external authoritative change
→ StateInvalidated
→ disposable frontend re-reads authoritative state
```

Do not pre-build:

```text
FeatureChanged<T>
per-feature event bus
revision counters
feature dirty bitmasks
observable state graph
```

If a future hardware-backed feature proves aggregate refresh too expensive, add targeted invalidation only after measurement demonstrates a real need.

---

## 19. Failure policy

### Runtime feature failure

Feature-local unless the feature's own contract says otherwise.

### Frontend pipe disconnect

Surface fails closed / disables stale mutation, Runtime remains alive.

### Overlay feature transport failure

Overlay shell/lifecycle remains usable; controller capture safety must follow existing OQ4/session-loss policy independently of feature state.

### Overlay process crash

Runtime remains controller authority; current Overlay capture/session-loss cleanup owns recovery. Shared frontend work must not invent a second recovery path.

### Main UI/QAM crash

No Runtime feature/controller authority changes.

---

## 20. Explicit non-goals

Do not implement or design toward:

- a generic settings engine;
- a dynamic feature registry;
- a plugin/provider framework;
- a global frontend state cache;
- a unified Main UI/QAM/Overlay renderer;
- one pipe shared by all processes;
- full `IAddonFrontendControl` exposure to Overlay;
- a fourth pipe endpoint;
- a generic RPC framework/reflection dispatcher;
- cross-feature locks/epochs/barriers;
- frontend ownership of hardware state;
- automatic surface parity;
- speculative placeholders for future features;
- Full1902 controller lifecycle changes.

---

## 21. Review standard

Block shared-frontend PRs for realistic issues such as:

- duplicate Runtime/hardware authority;
- stale editable UI after a real transport failure;
- one child failure discarding healthy sibling state;
- incorrect serialization/protocol handling;
- QAM-only admission policy leaking into shared Runtime semantics;
- accidental exposure of Main-UI-only operations through QAM/Overlay;
- Overlay feature traffic weakening OQ4 capture/close safety;
- transport teardown causing controller/presentation teardown;
- real resource leaks or deadlocks in normal lifecycle.

Do not block for theoretical adjacent-read races that converge through authoritative refresh, or demand locks/epochs/barriers without a realistic supported-product failure path.

---

## 22. Architecture target

The final target is intentionally simple:

```text
                         Addon Runtime
                              │
             one authority per real feature
                              │
                InProcessAddonFrontendControl
                              │
                    typed projections
                              │
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
   .Frontend              .Qam                 .Overlay
   full explicit          full explicit         narrow explicit
   RPC transport          RPC transport         lifecycle + approved
        │                     │                  quick-setting messages
        ↓                     ↓                     ↓
     Main UI               QamHost              Overlay
```

The reusable layer is **Runtime truth and typed semantics**.

The surface-specific layer is **exposure, transport policy, and UI**.

That separation is the Shared Frontend architecture.