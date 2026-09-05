# Steam Addon for Claw — Shared Frontend V2 Implementation PR Plan

> **Date:** 2026-09-05  
> **Status:** Current implementation roadmap  
> **Production code baseline reviewed:** `main` at `ed27976ff756ecb5bfc42569d642acb413b452a9` after PR #498  
> **Architecture authority:** `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md` as revised after PR #498  
> **Core product decision:** QAM and Addon Overlay share one Quick Settings product definition; only their renderer/admission/lifecycle remain surface-specific.

---

## 1. Goal

Finish Shared Frontend V2 so the Steam QAM and Addon Overlay do **not** become two separately maintained implementations of the same Device/Profile Quick Settings product.

The intended maintenance outcome is:

```text
change Device/Profile row name once
→ QAM + Overlay both change

change row order once
→ QAM + Overlay both change

change slider option/range once
→ QAM + Overlay both change

change debounce 2000 ms → 1000 ms once
→ QAM + Overlay both change

add a new shared Toggle/Slider feature once
→ both existing renderers show it
```

while preserving:

```text
Runtime = feature/hardware/persistence authority
Shared Quick Settings = product-definition projection only
QAM = Steam renderer + QAM admission/lifecycle
Overlay = WinUI renderer + OQ4 admission/lifecycle
Main UI = independent desktop management UI
```

---

## 2. Current baseline

### Completed foundation

SF-V2-01 and SF-V2-02 are complete.

#### SF-V2-01 — merged PR #496

Established:

```text
FrontendDeviceQuickSettingsSnapshot
CaptureDeviceQuickSettingsAsync
Main UI aggregate Device refresh
QAM aggregate Device refresh
```

#### SF-V2-02 — merged PR #498

Established `.Overlay` v6 Device feature transport:

```text
DeviceQuickSettingsState
DeviceMutationRequest
DeviceMutationResult
```

with:

- explicit eight-operation Device allowlist;
- visible/captured-session mutation admission;
- non-blocking slow mutation execution;
- `StateInvalidated` visible refresh;
- no polling;
- OQ4 Show/Hide/capture safety preserved.

### Current protocol versions

At the production baseline:

```text
FrontendTransportProtocol = 27
OverlayTransportProtocol  = 6
```

Frontend v27 comes from the later Claw Sensor Probe work merged after SF-V2-01.

Every future work order must re-check current versions immediately before coding. If an unrelated PR has already consumed the expected next version, increment from then-current source rather than forcing the numbers below.

### Current duplication to remove

QAM still hard-codes Device/Profile product semantics in `qam.js`, including:

```text
labels/order
control construction
CPU Boost option labels
Power Mode option labels
TDP local adjustment rules
feature-specific mutation method names
QAM_SLIDER_COMMIT_DELAY_MS = 2000
pending mutation keys
```

Overlay has separate primitives and:

```text
OverlayDelayedSliderCommit.ProductionDelay = 2000 ms
```

but no real Device binding yet.

This is the correct point to introduce the shared product layer before duplicated real Overlay feature UI is created.

---

## 3. PR size / review policy

Preferred target:

```text
~250–500 changed/added LOC per PR when practical
```

This is not a hard limit. Do not split one correctness invariant purely for LOC.

Prefer focused PRs because the repository is pre-release and the QAM JavaScript / Overlay C# renderers are materially different codebases.

Do not introduce merely for this plan:

```text
new process
new hardware owner
new settings database
new project/csproj unless dependency graph proves necessary
generic feature registry
plugin UI framework
reflection dispatcher
JSON form engine
new controller state machine
new lifecycle epochs/barriers
```

---

# Completed phases

## 4. SF-V2-01 — Device aggregate foundation — COMPLETE

Status:

```text
Merged PR #496
```

Keep its work order as a historical implementation record.

No further action except regression preservation.

---

## 5. SF-V2-02 — Overlay Device transport foundation — COMPLETE

Status:

```text
Merged PR #498
OverlayTransportProtocol = 6
```

The v6 Device-specific wire is intentionally allowed to be superseded before real Device UI binding because the product is pre-release and the shared Quick Settings decision was made after this foundation landed.

Do not delete the SF-V2-02 work order; it records the lifecycle/non-blocking invariants that the generic v7 replacement must preserve.

---

# Phase C — Shared Quick Settings product model

## 6. SF-V2-03 — Shared Quick Settings product contract + Device projection/dispatch

### Purpose

Create the one Runtime-side Quick Settings product definition without changing either visible surface yet.

This is the key architecture PR.

### Production scope

Add a small closed contract set under the existing frontend/contracts structure, conceptually:

```text
QuickSettingsPageId
QuickSettingsSectionId
QuickSettingsRowId
QuickSettingsControlKind
QuickSettingsSliderSpec
QuickSettingsOption
QuickSettingsCommitPolicy
QuickSettingsCommitGroupId
QuickSettingsLinkedSliderConstraint
QuickSettingsPageSnapshot
QuickSettingsMutationIntent
QuickSettingsMutationResult
```

Exact names may differ if the code is clearer.

### Initial identity vocabulary

Define identities for:

```text
Page:
  Device
  Profile
```

Device rows:

```text
DeviceTdpEnabled
DeviceTdpAcPl1
DeviceTdpAcPl2
DeviceTdpDcPl1
DeviceTdpDcPl2
DeviceCpuBoostEnabled
DeviceCpuBoostAc
DeviceCpuBoostDc
DevicePowerModeEnabled
DevicePowerModeAc
DevicePowerModeDc
```

Also reserve only the **currently intended visible Profile parity rows** needed by the immediately following Profile milestone so the generic wire does not need another schema change before Profile is activated. Do not include hidden/developer/future rows merely because Runtime methods exist.

At implementation time, re-check current `qam.js` and freeze the exact visible Profile row set. Current source does not justify automatically enabling the hidden `SHOW_INTEL_FPS_LIMIT = false` path.

### Device page projection

Add one stateless projection from:

```text
FrontendDeviceQuickSettingsSnapshot
        ↓
QuickSettingsPageSnapshot(Device)
```

The Device page definition becomes the single source for:

```text
section order
row order
labels
Toggle vs Slider
numeric TDP limits/steps
CPU Boost seven-option labels/order
Power Mode three-option labels/order
availability/writability
current authoritative value
commit policy
commit group
linked TDP constraints
```

### Commit policy

Freeze current behavior in the shared model:

```text
Toggle → Immediate
Slider → TrailingDebounce(2000 ms)
```

Do not yet remove QAM/Overlay constants in this PR because neither surface consumes the new model yet.

### TDP grouping

All four TDP numeric rows share:

```text
CommitGroupId = DeviceTdpConfiguration
```

The Runtime projection computes the existing TDP PL1/PL2 minimum-gap policy from current `FrontendTdpLimits` and expresses it as the narrow linked-slider constraint.

Move the **policy calculation** out of QAM-specific code into this shared projection.

### Central mutation adapter

Add one explicit Quick Settings mutation dispatch that maps validated Device intents onto the existing typed methods:

```text
SetDeviceCpuBoostEnabledAsync
SetDeviceCpuBoostAcAsync
SetDeviceCpuBoostDcAsync
SetDeviceTdpEnabledAsync
SetDeviceTdpAsync
SetDevicePowerModeEnabledAsync
SetDevicePowerModeAcAsync
SetDevicePowerModeDcAsync
```

Do not bypass these methods or duplicate their persistence/apply logic.

The generic mutation result should provide:

```text
Succeeded
FailureMessage
fresh authoritative Device QuickSettingsPageSnapshot
```

### IAddonFrontendControl seam

Add the smallest explicit methods needed, conceptually:

```text
CaptureQuickSettingsPageAsync(page/context)
MutateQuickSettingAsync(intent)
```

No frontend wire change yet.

Main UI does not migrate to these methods.

### Required tests

At minimum:

- Device page exact section/row order;
- exact labels;
- CPU Boost option ordering/labels;
- Power Mode option ordering/labels;
- TDP min/max/step from Runtime limits;
- TDP linked gap policy for known current limit shapes;
- partial child unavailability maps only affected rows unavailable;
- Toggle policy is Immediate;
- Slider policy is 2000ms trailing;
- all four TDP sliders share one commit group;
- each Device mutation intent reaches exactly one existing typed mutation method;
- malformed/mismatched value shape invokes zero mutations;
- grouped TDP request requires a complete valid group draft;
- result reprojects authoritative state;
- no hardware/state owner added.

### Expected protocol impact

```text
Frontend protocol: unchanged
Overlay protocol: unchanged
```

### Explicit non-goals

- no QAM renderer change;
- no Overlay renderer change;
- no `.Overlay` v7 yet;
- no Profile projection implementation yet;
- no Main UI change;
- no new manager/service.

---

# Phase D — QAM migration to the shared product

## 7. SF-V2-04 — Frontend/QAM generic Quick Settings RPC seam

### Purpose

Make the shared page/mutation contract reachable by QamHost without changing the visible QAM implementation in the same PR.

### Frontend transport

Add explicit RPC methods conceptually:

```text
CaptureQuickSettingsPage
MutateQuickSetting
```

Use the shared typed payloads from SF-V2-03.

Expected current-baseline bump:

```text
FrontendTransportProtocol 27 → 28
```

If current main has already moved beyond 27, bump exactly once from that value.

### QamFrontendBridge

Add a narrow JS allowlist path for the generic Quick Settings operations.

Preserve the existing surface admission:

```text
Device mutation
→ Big Picture active
→ AppId == 0
```

Do not move this policy into `IAddonFrontendControl` or the feature Runtime.

During this transition PR, the old feature-specific QAM bridge operations may remain temporarily because current `qam.js` still uses them.

Mark them for mandatory removal in SF-V2-05.

### Required tests

- frontend protocol current-version bump;
- page snapshot named-pipe round trip;
- mutation intent/result round trip;
- invalid page/row/value rejection;
- QAM Device admission still rejects running-game/non-BPM mutation;
- generic mutation reaches the same SF-V2-03 dispatch;
- old QAM Device UI remains behaviorally unchanged in this transition PR;
- no `.Overlay` change.

### Explicit non-goals

- no qam.js generic renderer yet;
- no Overlay change;
- no Profile migration yet.

---

## 8. SF-V2-05 — QAM Device generic renderer migration

### Purpose

Make Steam QAM the first real consumer of the shared Quick Settings product model and remove QAM as a second Device product-definition authority.

### QAM renderer

Refactor only the Device page path so it renders `QuickSettingsPageSnapshot(Device)` generically.

Map shared controls onto existing Steam UI primitives:

```text
Toggle → native ToggleField
Slider → native SliderField
Section → native PanelSection / PanelSectionRow as appropriate
```

Do not rewrite Steam webpack/component discovery.

Do not change the outer QAM tab injection architecture.

### Generic interaction engine

QAM JavaScript should have one generic pending draft/commit path driven by:

```text
RowId
CommitGroupId
CommitPolicy
linked constraint metadata
```

Observable behavior remains:

```text
slider preview immediate
latest draft wins
trailing delay from row policy
grouped TDP draft commits whole group
pending draft survives invalidation
authoritative mutation result wins
```

### Mandatory duplicate removal

Remove migrated Device hard-coding from `qam.js`, including product-owned copies of:

```text
QAM_SLIDER_COMMIT_DELAY_MS
Device labels/order
CPU Boost mode labels
Power Mode labels
TDP limit/gap product policy
feature-specific Device mutation method names
feature-specific pending keys
```

Remove the temporary old feature-specific Device bridge operations from `QamFrontendBridge` once no JS callsite uses them.

Do **not** remove the focused `NamedPipeAddonFrontendClient` typed feature methods used by Main UI/other code merely because QAM no longer calls them directly.

### UI parity requirement

This PR is a refactor of product definition ownership, not a QAM redesign.

The resulting Device page should preserve the current intended product content and order.

### Required tests

- qam.js has no hard-coded `QAM_SLIDER_COMMIT_DELAY_MS` product constant;
- Device row labels/order come from page payload;
- changing test page delay changes scheduler delay without JS source edit;
- Toggle uses Immediate policy;
- independent sliders use their own commit groups;
- four TDP rows share one pending group;
- linked TDP constraints apply to immediate local draft;
- invalidation preserves pending group draft;
- stale old completion cannot replace a newer draft;
- generic mutation response restores authoritative page state on success/failure;
- old Device bridge method names no longer appear in qam.js;
- no polling introduced;
- current QAM Profile behavior remains unchanged.

### Expected protocol impact

```text
No additional protocol bump after SF-V2-04.
```

---

# Phase E — Overlay generic transport + Device binding

## 9. SF-V2-06 — Replace `.Overlay` v6 Device wire with generic Quick Settings v7

### Purpose

Preserve everything SF-V2-02 proved about lifecycle/non-blocking behavior while replacing its Device-specific product API with the shared Quick Settings payload before real Device UI consumes v6.

### Wire replacement

Replace/supersede:

```text
DeviceQuickSettingsState
DeviceMutationRequest
DeviceMutationResult
OverlayDeviceMutationKind
OverlayDeviceMutationDispatch
```

with narrow generic Quick Settings messages conceptually:

```text
QuickSettingsPageState
QuickSettingsMutationRequest
QuickSettingsMutationResult
```

Reuse the shared page/intent/result contracts from SF-V2-03.

Keep transport-specific request correlation only where needed by the pipe.

Do not put request IDs into unrelated Show/Hide/Navigation/TabOrder messages.

### Protocol

Expected current-baseline bump:

```text
OverlayTransportProtocol 6 → 7
```

If another PR has already moved v6, increment from then-current.

### No compatibility dual-path

The product is pre-release and no real Overlay Device UI consumes v6.

Therefore do **not** retain both:

```text
old Device mutation API
+
new Quick Settings mutation API
```

inside current protocol.

Remove dead v6 production types/methods/tests as they are replaced, while preserving historical protocol comments.

### Preserve SF-V2-02 lifecycle invariants

The generic mutation operation must still:

- execute outside the sole Overlay read loop;
- use the existing single write gate;
- require Ready + Visible at transport level;
- require `_overlayCaptureActive` + not-shutting-down at Runtime admission;
- never delay OQ4 Hide/Dismiss processing on slow TDP hardware apply;
- ignore late retired request results correctly;
- publish state only after OQ4 capture commits;
- refresh from `StateInvalidated` only while appropriate;
- use no polling.

### Page publication scope

Initially publish only:

```text
Device
```

The generic wire must carry PageId so Profile can be added later without another transport redesign.

### Overlay App scope

Receive/store/forward generic page snapshots only.

Do not replace Device preview rows yet in this PR.

### Required tests

- v7 handshake / v6 rejection;
- Device page state round trip;
- strict mutation request/value validation;
- generic mutation dispatch reaches SF-V2-03 only after Overlay admission;
- slow mutation cannot block Hidden acknowledgement;
- `StateInvalidated` refresh only while visible/captured;
- state started visible but hidden before capture/send does not intentionally publish to hidden session;
- lifecycle/navigation/tab order unchanged;
- old v6 Device message path removed from current production code;
- no polling.

---

## 10. SF-V2-07 — Overlay generic Device renderer + real Device binding

### Purpose

Replace the temporary Device preview fixture with the real shared Device Quick Settings page.

After this PR, the Device milestone is complete.

### Renderer

Create the smallest Overlay-local generic page renderer/binder needed to map:

```text
Toggle → OverlayToggleRow
Slider → OverlaySliderRow
```

and register those rows with the existing OQ5 logical selection model.

Do not create a ViewModel framework, renderer plugin system, or global feature manager.

A small page-local renderer/binding helper is acceptable because it directly removes repeated feature UI code and is reused by Profile later.

### Delayed commit

Reuse `OverlayDelayedSliderCommit` mechanics where useful, but remove product ownership from it:

```text
ProductionDelay = 2000 ms
```

must no longer be the production policy source.

Pass the delay from each shared `CommitPolicy`.

If the existing helper's one-double/one-slider shape is insufficient for grouped TDP drafts, add the smallest generic **commit-group** layer above it or refine it narrowly. Do not add a scheduler service/manager.

### TDP

The renderer must consume the shared TDP group/linked-constraint metadata:

```text
4 rows
→ one local group draft
→ linked PL1/PL2 correction
→ one trailing group commit
```

No Overlay-specific duplicate of the Claw TDP gap policy.

### Toggle behavior

Toggle rows submit immediately according to shared policy.

When an immediate parent toggle disables a feature, any still-unsubmitted child slider draft for that feature/group must be retired so it cannot fire later against the newly disabled state.

Do not make OQ4 close wait for any draft/timer.

### Authoritative state

When a generic mutation result or `StateInvalidated` page arrives:

- non-pending rows adopt authoritative state;
- current pending group draft remains visible;
- current mutation settlement clears its group and applies authoritative page;
- failure must not leave a false committed value.

### Remove preview fixture

Delete/replace:

```text
Toggle Preview
Unavailable Toggle Preview
Slider Preview
Unavailable Slider Preview
Navigation Preview XX rows
```

from Device.

Do not change other tabs.

### Required hardware validation

On real MSI Claw:

- Device page order/labels match QAM;
- CPU Boost toggle + AC/DC modes;
- Power Mode toggle + AC/DC modes;
- TDP toggle + AC/DC PL1/PL2;
- 2-second current shared delay observed on sliders;
- rapid left/right sends only latest grouped/row commit;
- touch and controller both work;
- reopen shows Runtime truth;
- QAM and Overlay agree after mutation;
- B close / outside click / hide never wait for debounce;
- game/Steam input remains neutral while Overlay capture is active.

### Expected protocol impact

```text
No bump after SF-V2-06.
```

---

# Phase F — Profile parity

## 11. SF-V2-08 — Shared Profile projection/dispatch + QAM generic Profile migration

### Purpose

Extend the already-proven shared model to the current QAM Profile product, then migrate QAM Profile onto the same generic renderer.

### Freeze actual visible scope first

Immediately before coding, inspect current `qam.js` and current Profile contracts.

The implementation must mirror the **actually intended visible QAM Profile controls**, not every mutation method in `QamFrontendBridge`.

Current source has mutation capability for:

```text
Profile enabled
TDP
CPU Boost
Windows Power Mode
Intel FPS Limit
```

and `FrontendGameProfileSnapshot` also contains Resolution.

But current `qam.js` keeps Intel FPS Limit behind:

```text
SHOW_INTEL_FPS_LIMIT = false
```

Do not make hidden capability visible simply because the generic renderer can render it.

### Shared Profile projection

Build:

```text
FrontendGameProfileSnapshot
        ↓
QuickSettingsPageSnapshot(Profile)
```

with:

- same labels/order/control kinds for both surfaces;
- AppId context;
- same commit policies/groups;
- same typed options/limits;
- clear no-active-game/unavailable state.

### Mutation dispatch

Map Profile row intents centrally onto the existing typed Game Profile mutation methods.

Validate the request AppId/current target before mutation.

No game scanning in QAM/Overlay.

No second Profile store/model.

### QAM migration

Replace the feature-specific Profile React construction with the same generic Quick Settings renderer used by Device.

Page-selection behavior remains QAM-specific:

```text
no active game → Device shared page
active game    → Profile shared page
```

### Required tests

- exact Profile visible row set frozen from current policy;
- AppId carried in page context;
- stale AppId mutation rejected;
- no-active-game page is explicit;
- QAM Profile uses generic renderer;
- shared debounce/group behavior works identically to Device;
- hidden FPS path does not accidentally become visible;
- no duplicated Profile product labels/policies remain in qam.js for migrated rows;
- Device behavior unchanged.

### Expected protocol impact

If SF-V2-03 included the required current Profile identity vocabulary and the generic page/value contract is unchanged:

```text
No protocol bump expected.
```

If implementation discovers that a genuinely new wire shape/control kind is required, stop and bump the affected protocol explicitly rather than smuggling incompatible fields through the existing version.

---

## 12. SF-V2-09 — Overlay Profile publication + generic binding

### Purpose

Make the existing Overlay Profile tab render the same shared Profile product with no new feature-specific Overlay UI implementation.

### Runtime publication

While Overlay is visible/captured, publish current shared pages event-driven:

```text
Device
Profile
```

on successful Show and relevant `StateInvalidated` refresh.

Publishing both low-rate pages is preferred over adding page-selection IPC/polling solely to save one small projection.

### Overlay renderer

Reuse the generic renderer/binder from SF-V2-07.

No new Profile-specific control construction should be required for Toggle/Slider rows.

If there is no active game/profile:

```text
Profile tab remains visible
→ deliberate unavailable/empty state
→ no mutation target
```

Do not alias Device state into the Profile tab.

### Profile mutation admission

Overlay admission remains:

```text
Ready + Visible + _overlayCaptureActive + not shutting down
```

Then the shared Profile mutation adapter validates the AppId context.

### Required validation

- current running Steam game shows same Profile rows/order/labels as QAM;
- no game shows explicit unavailable state;
- Profile toggle/sliders mutate the same Runtime methods as QAM;
- stale active-game change does not mutate wrong AppId;
- Device and Profile pending drafts remain isolated;
- B/Hide/OQ4 lifecycle remains correct during pending/submitted Profile mutation;
- no polling;
- no protocol bump if v7 generic contract already carries Profile page data.

---

# Phase G — Future shared Quick Settings features

## 13. Extension rule after SF-V2-09

Once Device/Profile are migrated, a future QAM+Overlay parity feature using existing Toggle/Slider controls should normally require only:

```text
Runtime feature/typed contract
        ↓
shared Quick Settings projection row(s)
        ↓
central mutation mapping
        ↓
contract tests
```

The two renderers should not need feature-specific edits.

Examples that may later qualify after their Runtime contracts/product policy exist:

```text
Fan Control
Battery Charge Limit
Controller vibration strength
Joystick LED if its shared UI can be expressed by an approved control kind
```

Do not add placeholders now.

### New control kind

If a future shared feature genuinely requires a new kind such as Choice:

```text
1. add one explicit shared control kind/spec
2. add one QAM renderer adapter
3. add one Overlay renderer adapter
4. test parity
```

After that, features using that kind should again be data-driven.

Do not evolve toward arbitrary form schemas.

---

# Cross-PR invariants

## 14. Main UI stays outside generic Quick Settings rendering

Main UI continues to use existing typed feature contracts and its desktop layout.

Do not migrate Center M authority or desktop management flows into Quick Settings.

`FrontendDeviceQuickSettingsSnapshot` remains useful beneath both Main UI and shared Device projection.

---

## 15. Transport boundaries remain separate

Final intended shape:

```text
.Frontend
→ full explicit frontend RPC
→ Main UI

.Qam
→ full explicit frontend RPC
→ QamFrontendBridge allowlist
→ shared Quick Settings renderer

.Overlay
→ narrow lifecycle/navigation/Quick Settings protocol
→ shared Quick Settings renderer
```

Do not merge the pipe endpoints.

Do not let Overlay connect to `.Qam` or `.Frontend`.

Do not expose full `IAddonFrontendControl` to Overlay.

---

## 16. Surface admission remains surface-specific

Shared rows do not imply shared admission.

QAM Device:

```text
BPM + no running game
```

Overlay:

```text
Ready + Visible + capture committed + not shutting down
```

Profile additionally validates AppId context at the shared mutation boundary.

---

## 17. Refresh remains event-driven

Use:

```text
initial authoritative page
+ StateInvalidated re-projection
+ mutation-result authoritative page
```

Do not add a feature polling timer.

Sensor/diagnostic polling remains unrelated developer functionality.

---

## 18. OQ4 lifecycle safety is higher priority than Quick Settings feature work

Never insert feature capture/mutation waits into the critical Show sequence before presentation neutralization/capture commit.

Never make Hide/retirement wait for:

```text
debounce timer
TDP hardware apply
feature refresh
```

Slow feature work remains feature-local and async from the Overlay read loop as established by SF-V2-02.

---

## 19. No overengineering for theoretical races

Preserve realistic protections:

- current delayed commit generation/token;
- request correlation on the pipe;
- profile AppId target check;
- current Runtime owner gates;
- OQ4 capture admission;
- authoritative result/readback.

Do not add:

```text
snapshot epochs
revision vectors
cross-feature transactions
barriers
global mutation scheduler
feature graph manager
```

for artificial instruction-level interleavings that do not represent supported lifecycle failures.

---

# Verification strategy

## 20. Per-PR verification

Every implementation PR should run at minimum:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

No new warnings.

Use focused tests first, full regression before completion.

---

## 21. Parity regression tests

Add durable tests that make the maintenance goal enforceable.

Required direction:

```text
shared Device snapshot contract
→ exact row/section/order/policy tests

QAM renderer policy test
→ consumes shared row metadata
→ no Device product delay/label duplication

Overlay renderer policy test
→ consumes shared row metadata
→ no Device product delay/label duplication
```

For a policy such as debounce delay, tests should make it impossible to silently reintroduce two product constants.

The shared contract test should be the one place asserting the production value.

---

# Dependency graph

## 22. Recommended sequence

```text
COMPLETE
SF-V2-01  Device typed aggregate / Main UI + QAM read foundation
        │
        ▼
COMPLETE
SF-V2-02  Overlay Device v6 transport foundation
        │
        ▼
SF-V2-03  Shared Quick Settings product contract
          + Device projection
          + central Device mutation dispatch
        │
        ▼
SF-V2-04  Frontend/QAM generic Quick Settings RPC
          Frontend protocol current → next
        │
        ▼
SF-V2-05  QAM Device generic renderer migration
          remove QAM Device product duplication
        │
        ▼
SF-V2-06  Overlay generic Quick Settings v7 transport
          remove v6 Device-specific current API
        │
        ▼
SF-V2-07  Overlay Device generic renderer/binding
          Device parity milestone complete
        │
        ▼
SF-V2-08  Shared Profile projection/dispatch
          + QAM Profile generic migration
        │
        ▼
SF-V2-09  Overlay Profile publication/binding
          Profile parity milestone complete
```

Prefer this sequential order for the first implementation because it validates the shared model on the mature QAM surface before deleting the v6 Overlay feature API and binding real Overlay controls.

After SF-V2-03 stabilizes, some transport work is technically separable, but parallel implementation is not necessary and may create avoidable shared-contract churn.

---

## 23. Why seven follow-up PRs is reasonable

The codebase is pre-release and the user-visible product semantics are still being consolidated.

The split keeps distinct review questions separate:

```text
03: Is the shared product model correct and small?
04: Is QAM transport/admission correct?
05: Does QAM render the model without product duplication?
06: Does Overlay transport preserve OQ4/SF-V2-02 lifecycle safety?
07: Does WinUI render/operate the same Device model?
08: Does Profile map correctly and keep AppId safety?
09: Does Overlay reuse Profile without another UI implementation?
```

Combining these into one large PR would make it difficult to distinguish product-model defects from transport/lifecycle defects.

---

# Milestones

## 24. Device parity milestone — after SF-V2-07

Complete when:

```text
Runtime CPU/TDP/Power authorities
        ↓
FrontendDeviceQuickSettingsSnapshot
        ↓
shared Device Quick Settings product page
        ↓
QAM generic renderer + Overlay generic renderer
```

and:

- one shared row order/label definition;
- one shared 2000ms current commit policy;
- one shared CPU/Power option definition;
- one shared TDP gap/group policy;
- one central Quick Settings mutation mapping;
- QAM Device hard-coded feature implementation removed;
- Overlay Device preview removed;
- both surfaces show/operate the same Device product;
- Main UI remains independent;
- no new feature authority/cache/pipe endpoint exists.

---

## 25. Profile parity milestone — after SF-V2-09

Complete when:

- one shared Profile page definition exists;
- QAM and Overlay render the same approved Profile controls;
- no active game is explicit;
- AppId context prevents stale wrong-game mutation;
- QAM Profile feature-specific UI duplication is removed;
- Overlay Profile adds no second feature-specific implementation;
- hidden/unapproved Profile capability remains hidden;
- no independent game detector/store exists in Overlay.

---

## 26. Final maintenance acceptance test

After both milestones, the following should be true in code review:

> **A request such as “change shared Quick Settings slider debounce from 2 seconds to 1 second” is implemented by changing the shared product policy and its tests, without editing QAM-specific or Overlay-specific feature definitions.**

Likewise, adding a new shared Toggle/Slider row should not require adding one feature-specific QAM renderer path and one feature-specific Overlay renderer path.

If that is not true, the architecture has not achieved its primary maintenance goal.

---

## 27. Next action

Prepare the focused work order for:

```text
SF-V2-03 — Shared Quick Settings product contract + Device projection/dispatch
```

against the latest `main` immediately before implementation.

Do not begin the old planned `SF-V2-03 — Overlay CPU Boost + Power Mode binding`; that plan is superseded by this shared QAM/Overlay product-model architecture.
