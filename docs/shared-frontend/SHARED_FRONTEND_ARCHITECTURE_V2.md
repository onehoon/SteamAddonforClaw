# Steam Addon for Claw — Shared Frontend Architecture V2

> **Date:** 2026-09-05  
> **Status:** Current design authority for shared Runtime/frontend projection and shared Quick Settings product semantics  
> **Baseline reviewed:** `main` at `ed27976ff756ecb5bfc42569d642acb413b452a9` after PR #498  
> **Scope:** Addon Runtime, desktop Main UI, Steam QAM, Addon Quick Settings Overlay, shared typed feature state, shared Quick Settings page semantics, surface-specific transport/admission, and renderer boundaries

---

## 1. Purpose

The Addon now has three materially different frontend surfaces:

```text
SteamInputAddonforClaw.UI.exe
→ desktop WinUI management/configuration UI

SteamInputAddonforClaw.QamHost.exe
→ Steam GamepadUI / CEF QAM integration

SteamInputAddonforClaw.Overlay.exe
→ Addon-owned native WinUI Quick Settings overlay
```

The original Shared Frontend V2 decision was correct in one important way:

```text
Runtime feature truth must be shared.
Frontend processes must remain disposable.
```

However, current product direction is now more specific for **Steam QAM and Addon Overlay**:

> For shared Quick Settings pages such as Device and Profile, QAM and Overlay are intended to expose the **same product controls with the same names, order, value semantics, mutation behavior, and debounce policy**. The only intentional difference is how each surface renders and navigates those controls.

Therefore the architecture must share one more layer than the previous version described.

The target is now:

```text
one Runtime authority per real feature
        ↓
one typed frontend feature projection
        ↓
one shared Quick Settings product definition
        ↓
explicit surface admission + transport
        ↓
QAM renderer / Overlay renderer
```

This is **not** a generic UI framework. It is a narrow shared product-definition layer for the Quick Settings surfaces that are intentionally kept in parity.

---

## 2. Required authority documents

Read this document together with the current Full1902 authority set in its documented precedence order:

1. `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
2. `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
3. `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
4. current `docs/work-order/*` implementation work orders/addenda

Also read:

- `docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md`
- `docs/shared-frontend/SF_V2_01_DEVICE_QUICK_SETTINGS_SHARED_AGGREGATE_WORK_ORDER.md`
- `docs/shared-frontend/SF_V2_02_OVERLAY_DEVICE_QUICK_SETTINGS_TRANSPORT_WORK_ORDER.md`
- `docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md`

SF-V2-01 and SF-V2-02 are completed implementation records. Where their future-UI planning differs from this document, this document is the current architecture authority.

---

## 3. Frozen Full1902 / lifecycle invariants

Shared frontend and Quick Settings work must not become controller authority.

The following remain Runtime-owned and outside this architecture layer:

```text
Center M Enabled / Disabled authority
PID1901 / PID1902 ownership
DirectInput physical ownership
HidHide deterministic baseline
VIIPER native/runtime ownership
Xbox360 / SteamDeck presentation ownership
physical-device loss / PnP recovery
sleep / hibernate / resume handling
restart / shutdown teardown
OQ4 controller capture / neutral publication
Full1902 fail-close and stock-restoration paths
```

Independent Device features such as TDP, CPU Boost, Power Mode, future fan control, and future battery charge limit may continue regardless of controller presentation where their own feature contract permits it.

Frontend lifetime remains separate from Runtime lifetime:

```text
QAM closes
→ Runtime survives

Overlay closes/crashes
→ Runtime survives
→ existing OQ4/session-loss path owns capture cleanup

Main UI closes
→ Runtime survives
```

No shared Quick Settings component may acquire or restore PID1901/PID1902, touch HidHide, own VIIPER, or become a controller recovery participant.

---

## 4. Current production baseline after SF-V2-01 / 02

### 4.1 One Runtime/frontend projection already exists

`AddonProcessHost` owns one production `InProcessAddonFrontendControl`.

Existing Runtime feature authorities are already projected through it, including:

```text
CPU Boost
TDP
Windows Power Mode
Game Profile
Intel FPS Limit
Center M startup authority
front-button settings
```

Do not add a second feature owner such as:

```text
QuickSettingsRuntime
SharedFrontendRuntime
OverlayFeatureManager
QamFeatureManager
SharedDeviceStateCache
FrontendAuthorityManager
```

### 4.2 SF-V2-01 is complete

PR #496 established:

```text
FrontendDeviceQuickSettingsSnapshot
├─ CpuBoost
├─ Tdp
└─ PowerMode
```

and `CaptureDeviceQuickSettingsAsync()`.

Main UI and QAM now read this aggregate instead of independently capturing the three Device features for normal Device refresh.

The aggregate remains a typed read projection only. It is not presentation metadata and not a new feature authority.

### 4.3 SF-V2-02 is complete

PR #498 established `.Overlay` Device transport v6:

```text
DeviceQuickSettingsState
DeviceMutationRequest
DeviceMutationResult
```

for the eight approved Device mutations.

The v6 transport correctly preserves:

- one `_frontendControl` authority;
- visible/captured Overlay admission;
- non-blocking mutation execution so slow TDP apply cannot block Hide/Dismiss/other read-loop traffic;
- `StateInvalidated`-driven visible-session refresh;
- OQ4 capture-before-feature-publish safety;
- no polling.

This v6 Device transport is an important foundation, but it was deliberately created before real Device UI binding. Therefore it may be superseded cleanly by the shared Quick Settings wire before product UI consumes it.

### 4.4 Current protocol versions

At this baseline:

```text
FrontendTransportProtocol.CurrentVersion = 27
OverlayTransportProtocol.CurrentVersion  = 6
```

Frontend v27 includes later unrelated Claw Sensor Probe work after SF-V2-01 claimed v26.

Do not use stale `25`/`26` planning numbers when preparing future work orders.

### 4.5 Current duplication now matters

QAM and Overlay currently still encode Quick Settings interaction policy separately.

QAM currently hard-codes, among other things:

```text
QAM_SLIDER_COMMIT_DELAY_MS = 2000
Device row names/order
CPU Boost mode labels
Windows Power Mode labels
TDP PL1/PL2 adjustment policy
per-feature mutation method names
pending-draft keys
```

Overlay currently has a separate:

```text
OverlayDelayedSliderCommit.ProductionDelay = 2000 ms
```

and temporary preview rows rather than real Device controls.

If real Overlay Device/Profile binding continues from here without another shared layer, every product change would need to be repeated in QAM JavaScript and Overlay C#.

That is the duplication this revision removes.

---

## 5. Product decision: QAM + Overlay are two renderers of the same Quick Settings product

For a page admitted to the shared Quick Settings set, QAM and Overlay must agree on:

```text
page identity
section order
row order
row label
control kind
current authoritative value
available / writable state
numeric min / max / step
ordered discrete options and labels
mutation identity
commit mode
debounce delay
mutation grouping
pending-draft behavior
linked-value constraints
authoritative settlement/readback behavior
```

They may differ only in surface implementation details such as:

```text
Steam React / CEF controls vs WinUI 3 controls
Steam PanelSection chrome vs Overlay row chrome
QAM focus/navigation vs OQ4 semantic Overlay navigation
surface-specific busy/error presentation details
surface-specific admission and lifetime
```

The intended invariant is:

> **If a user sees the shared Device page in QAM and in Overlay, the product controls and their behavior are the same even though the pixels/widgets are rendered by different UI technologies.**

---

## 6. Main UI is intentionally not part of the shared Quick Settings renderer contract

The desktop Main UI remains the superset management/configuration surface.

Current Main UI Device ownership includes items such as:

```text
Device identity/support
Center M authority
TDP
CPU Boost
Windows Power Mode
future Fan Control
future Battery Charge Limit
```

The Main UI may use cards, expanders, ComboBox controls, explanations, confirmation dialogs, or reboot-bound flows that do not belong in a handheld Quick Settings surface.

Therefore the architecture is:

```text
Runtime typed feature contracts
        ├─ Main UI
        │    → existing desktop-specific UI
        │
        └─ Shared Quick Settings product model
             ├─ Steam QAM renderer
             └─ Addon Overlay renderer
```

Do not force the Main UI to become schema-rendered merely to reduce code.

---

## 7. Shared Quick Settings product model

The shared model is a **closed, typed presentation contract** generated from Runtime-owned typed feature snapshots.

It is not persisted.

It does not read hardware directly.

It does not own feature state.

It does not replace `FrontendDeviceQuickSettingsSnapshot` or `FrontendGameProfileSnapshot`.

A practical conceptual shape is:

```text
QuickSettingsPageSnapshot
├─ PageId
├─ Context
├─ Message / availability summary
└─ Sections[]
     └─ QuickSettingsSectionSnapshot
          ├─ SectionId
          ├─ Label?              // product text, optional
          └─ Rows[]
               └─ QuickSettingsRowSnapshot
                    ├─ RowId
                    ├─ Label
                    ├─ ControlKind
                    ├─ Available
                    ├─ Writable
                    ├─ Value
                    ├─ SliderSpec? / options?
                    ├─ CommitPolicy
                    └─ CommitGroupId?
```

Exact record names may vary, but the semantics above are frozen.

### 7.1 Page identity

Initial implemented page IDs should be only those already proven product surfaces:

```text
Device
Profile
```

Do not pre-add Controller/Shortcut/Setting page IDs merely because the Overlay has those tabs.

Add another shared page only when QAM + Overlay genuinely share its contents.

### 7.2 Section identity

Sections preserve product grouping and order without prescribing visual chrome.

Example Device grouping:

```text
Device
├─ TDP
├─ CPU Boost
└─ Windows Power Mode
```

QAM may render these as Steam `PanelSection`s.

Overlay may render the same sections as simple grouped rows with no extra card chrome.

Both must preserve the shared order.

### 7.3 Row identity

Use an explicit enum, not strings as feature authority.

Conceptually:

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

Later Profile row IDs are likewise explicit.

Do not use:

```text
"setDeviceCpuBoostAc"
"feature:tdp/ac/pl1"
arbitrary plugin keys
reflection names
```

as the product contract.

### 7.4 Supported control kinds

Start with only the controls actually required now:

```text
Toggle
Slider
```

A slider spec may be either:

```text
Numeric
→ min / max / step / display suffix

Discrete
→ ordered value + label options
```

This covers current Device Quick Settings:

- TDP numeric watt sliders;
- CPU Boost seven-step discrete sliders;
- Windows Power Mode three-step discrete sliders.

Do not introduce `Choice`, `Action`, `Color`, `KeyBinding`, nested editors, or generic form schemas before a real shared feature requires them.

When a genuinely new control kind is needed later, add it deliberately to the shared contract and implement one renderer adapter in QAM and one in Overlay.

---

## 8. Shared labels, order, options, and value semantics

The Runtime-side Quick Settings projection is the single source for product presentation semantics.

For Device, it should define the existing QAM product layout centrally, for example:

```text
TDP Control                    Toggle
Plugged in · PL1              Numeric slider
Plugged in · PL2              Numeric slider
On battery · PL1              Numeric slider
On battery · PL2              Numeric slider

CPU Boost                     Toggle
Plugged in                    Discrete slider
On battery                    Discrete slider

Windows Power Mode            Toggle
Plugged in                    Discrete slider
On battery                    Discrete slider
```

CPU Boost discrete options are centrally defined:

```text
Disabled
Enabled
Aggressive
Efficient Enabled
Efficient Aggressive
Aggressive At Guaranteed
Efficient Aggressive At Guaranteed
```

Windows Power Mode discrete options are centrally defined:

```text
Best power efficiency
Balanced
Best performance
```

If a product label, row order, option label, or numeric step changes, the shared projection changes once and both renderers receive the same result.

No Device/Profile product labels should remain independently hard-coded in both QAM and Overlay after migration.

---

## 9. Shared commit policy

Commit timing is product behavior, not renderer policy.

Represent it in the shared row contract.

Conceptually:

```text
QuickSettingsCommitPolicy
├─ Immediate
└─ TrailingDebounce(delayMs)
```

Current Device policy is:

```text
Toggle
→ Immediate

Slider
→ TrailingDebounce(2000 ms)
```

The important outcome is:

```text
change 2000 ms → 1000 ms in the shared product definition
→ QAM uses 1000 ms
→ Overlay uses 1000 ms
→ no surface-specific policy edit
```

After migration:

- QAM must not own a product constant such as `QAM_SLIDER_COMMIT_DELAY_MS`;
- Overlay must not own a product constant such as `OverlayDelayedSliderCommit.ProductionDelay`.

The generic surface interaction engines may still implement the mechanics of a timer/generation in their own language, but the **policy and delay value** come from the shared row snapshot.

---

## 10. Pending draft behavior is shared product behavior

Both surfaces must implement the same observable rule:

```text
user edits slider
→ visible draft updates immediately
→ authoritative mutation is delayed according to CommitPolicy

another edit before delay expires
→ old unsubmitted commit is replaced
→ newest draft remains visible
→ timer restarts

StateInvalidated / fresh authoritative snapshot arrives while draft is pending
→ pending local draft remains visible for that commit group
→ unrelated rows adopt new Runtime state

current commit settles
→ pending group clears
→ authoritative page/result becomes visible
```

The implementation is intentionally surface-local because QAM is JavaScript and Overlay is C#.

But it must be **generic per renderer**, driven by shared row/group metadata, not reimplemented feature-by-feature.

A small generation/token protecting a current delayed commit is still appropriate for normal async I/O. Do not generalize it into epochs/barriers/revision vectors.

---

## 11. Mutation groups

Some slider edits are logically independent.

Examples:

```text
DeviceCpuBoostAc
DeviceCpuBoostDc
DevicePowerModeAc
DevicePowerModeDc
```

Each may use its own commit group.

TDP is different.

Current product behavior treats all four TDP sliders as edits to one configuration:

```text
AC PL1
AC PL2
DC PL1
DC PL2
        ↓
one FrontendTdpConfiguration draft
        ↓
one trailing commit group
```

Represent this explicitly with a shared commit-group identity.

Conceptually:

```text
CommitGroupId = DeviceTdpConfiguration
```

for all four TDP sliders.

A new edit to any member restarts the same trailing window and the submitted intent carries the latest whole TDP draft.

This preserves current QAM behavior while making it generic and available to Overlay without separate TDP debounce architecture.

---

## 12. Linked slider constraints — narrow support only

Current QAM contains TDP-specific local PL1/PL2 correction logic, including the current Claw limit-dependent minimum gap policy.

That rule must not remain duplicated in QAM JavaScript and future Overlay C#.

Move the **policy data** into the shared Runtime projection.

A narrow linked-slider constraint is sufficient, conceptually:

```text
LowerRow = AC PL1
UpperRow = AC PL2
MinimumGapWatts = computed from current Runtime TDP limits
```

and likewise for DC.

The projection computes the current minimum gap once from the real TDP limit contract.

The two generic renderers apply one small linked-slider rule when updating a local draft.

Do not build:

```text
expression trees
arbitrary validation DSL
formula engine
schema-driven dependency graph
```

The only required initial linked-value rule is the proven TDP PL1/PL2 relationship.

If a later feature needs a materially different constraint, add another explicit typed rule only then.

---

## 13. Shared mutation intent and central dispatch

UI parity is incomplete if QAM and Overlay still maintain separate feature-name-to-Runtime mappings.

Add one closed Quick Settings mutation intent.

Conceptually:

```text
QuickSettingsMutationIntent
├─ PageId
├─ ContextId
├─ EditedRowId
└─ Values[]
     ├─ RowId
     └─ typed value
```

For a toggle or independent slider, `Values` contains one row.

For a grouped TDP commit, `Values` contains the complete current group draft.

The value representation must be closed and validated, for example a strict union of:

```text
Boolean
Integer/discrete value
Number
```

Do not use arbitrary JSON/object payloads as the feature contract.

### 13.1 One explicit Runtime mapping

One central dispatch maps `QuickSettingsRowId` / group intent onto the existing typed frontend methods:

```text
DeviceCpuBoostEnabled
→ SetDeviceCpuBoostEnabledAsync

DeviceCpuBoostAc
→ SetDeviceCpuBoostAcAsync

DeviceTdpConfiguration group
→ SetDeviceTdpAsync

DevicePowerModeDc
→ SetDevicePowerModeDcAsync
```

and later Profile IDs map to the existing Game Profile mutation methods.

This dispatch does **not** replace those typed feature methods.

It is only the Quick Settings adapter onto them.

No new hardware reader, persistence path, or feature authority is created.

### 13.2 Authoritative mutation result

The generic Quick Settings mutation result should return enough information for a renderer to reconcile without feature-specific knowledge.

Preferred semantics:

```text
Succeeded / FailureMessage
+ fresh authoritative QuickSettingsPageSnapshot
```

A typed feature mutation remains the underlying operation and retains its own internal outcome semantics.

The Quick Settings adapter converts that outcome into the generic surface result and reprojects current page state.

This means the renderer never needs to know what `FrontendCpuBoostMutationResult` versus `FrontendTdpMutationResult` looks like.

---

## 14. Profile context safety

Device scope has no game identity.

Profile scope does.

A shared Profile page snapshot must carry the active target identity, at minimum:

```text
AppId
```

A Profile mutation intent must carry the same context.

Before a Runtime Profile mutation is invoked, the shared adapter must verify the target is still the intended active/valid profile target under the existing product rules.

This protects the realistic sequence:

```text
Profile page shown for Game A
→ game context changes to Game B
→ stale UI submits a delayed mutation
```

The mutation must not silently apply Game A's delayed draft to Game B.

Do not add an epoch/revision counter for this. The real stable product identity is the AppId already present in the profile contract.

---

## 15. Surface admission remains separate

Shared product semantics do not mean shared admission.

### QAM Device admission

Keep current QAM policy:

```text
Steam Big Picture active
AND no running Steam AppId
→ Device mutation admitted
```

QAM may decide to show Profile instead of Device when a game is active.

### Overlay admission

Keep current `.Overlay` / OQ4 policy:

```text
Overlay connection Ready
AND Visible
AND _overlayCaptureActive
AND process not shutting down
→ Quick Settings mutation admitted
```

The Overlay Device tab remains Device/global scope even while a game is running.

### Main UI

Main UI retains its own desktop UI admission/persistence behavior and does not route through the shared Quick Settings renderer contract.

Do not move QAM or Overlay admission into Runtime feature implementations.

---

## 16. Transport architecture

Transport lifetimes remain intentionally different.

```text
.Frontend / .Qam
→ full explicit frontend RPC transport

.Overlay
→ dedicated narrow lifecycle/navigation/Quick Settings transport
```

The shared Quick Settings payload contracts may be reused by both transports.

The transports themselves remain separate.

### 16.1 Frontend/QAM transport

Add explicit frontend RPC operations conceptually equivalent to:

```text
CaptureQuickSettingsPage
MutateQuickSetting
```

QAM uses these through its existing `QamFrontendBridge` allowlist.

Main UI need not use them.

Adding these wire operations requires a normal `FrontendTransportProtocol` version bump from the current version at implementation time.

### 16.2 Overlay transport

Before real Device UI binding, replace/supersede the v6 Device-specific wire with generic Quick Settings messages, conceptually:

```text
QuickSettingsPageState
QuickSettingsMutationRequest
QuickSettingsMutationResult
```

The existing Overlay lifecycle/navigation/tab-order messages remain unchanged.

Because v6 is pre-release and no production Device UI consumes it yet, do **not** keep both Device-specific and generic Quick Settings mutation APIs indefinitely for compatibility.

Prefer:

```text
v6 Device transport
→ superseded by v7 Quick Settings transport
→ remove dead v6 Device message/types/methods
```

rather than maintaining two parallel ways to change the same Device feature.

### 16.3 Overlay remains narrow

Generic here means **generic only within the closed Quick Settings contract**.

Do not expose:

```text
full IAddonFrontendControl
Developer probes
Center M authority transition
prerequisite repair
environment reports
arbitrary method names
reflection dispatch
```

through `.Overlay`.

The Runtime binding still explicitly allows only approved Quick Settings pages/rows.

---

## 17. Runtime-side product projection is the one source of page content

The preferred implementation is one small stateless projection adjacent to the existing frontend layer, for example conceptually:

```text
QuickSettingsPresentation
BuildDevice(FrontendDeviceQuickSettingsSnapshot)
BuildProfile(FrontendGameProfileSnapshot)
```

and one explicit mutation adapter.

This should live in the existing Runtime/frontend codebase rather than creating a new process or service.

Do not create a new project solely to hold these two pure mappings unless the actual dependency graph proves it necessary.

The projection owns no mutable state.

It simply turns current typed feature truth into shared Quick Settings product rows.

---

## 18. QAM renderer boundary

After migration, `qam.js` should become a generic Quick Settings renderer/interaction adapter rather than a second Device/Profile product definition.

It may own:

```text
Steam component discovery
Steam React element creation
Steam PanelSection / ToggleField / SliderField mapping
surface-local transient drafts
timers/generation mechanics driven by row CommitPolicy
bridge request plumbing
QAM-specific focus/navigation
```

It should not own:

```text
Device/Profile product row names
row order
CPU Boost option labels
Power Mode option labels
2000 ms product delay
TDP limit/gap policy
feature-specific mutation method names for migrated Quick Settings rows
```

A page snapshot should be enough to render and interact with the page.

---

## 19. Overlay renderer boundary

Overlay should use the same page model with its existing primitives:

```text
Toggle row
→ OverlayToggleRow

Slider row
→ OverlaySliderRow
```

The Overlay renderer may own:

```text
WinUI row creation
OQ5 logical selection registration
pointer/touch event hookup
OQ4 semantic Left/Right/A navigation
surface-local draft/timer/generation mechanics driven by row CommitPolicy
scroll-into-view behavior
```

It should not own the migrated product labels/order/policy.

`OverlayDelayedSliderCommit` may remain as a narrow timing helper, but its delay must come from the shared row policy rather than `ProductionDelay`.

If grouped TDP drafts require a small generic page interaction helper, keep it page-local and data-driven. Do not create a global mutation manager/service.

---

## 20. State refresh and invalidation

Keep event-driven authoritative refresh.

### QAM

```text
initial page capture
+ StateInvalidated-driven re-capture
+ mutation-result authoritative page
```

### Overlay

```text
successful OQ4 capture commit
→ Runtime best-effort publishes current shared page state

StateInvalidated while captured/visible
→ Runtime republishes current shared page state

hidden
→ no feature polling
```

When Profile becomes shared, Runtime may publish both Device and Profile pages while Overlay is visible. This is acceptable because the number of shared pages is small and refresh is event-driven.

Do not add page-selection IPC or high-frequency polling merely to avoid one low-rate extra projection.

If measurement later proves projection cost material, optimize from evidence.

---

## 21. Failure behavior

### Feature capture failure

The shared page projection renders affected rows unavailable while healthy siblings remain usable where the underlying typed contract permits it.

### Mutation returns typed feature failure

Convert it to the generic Quick Settings mutation result:

```text
Succeeded = false
FailureMessage = feature result message
Page = fresh authoritative projection
```

Do not tear down QAM/Overlay or controller capture.

### Transport failure

The affected surface stops trusting editable stale state and fails closed locally.

Runtime feature/controller authority survives.

### Overlay process/pipe failure while captured

Existing OQ4/session-loss retirement owns capture recovery.

A Quick Settings feature failure must never be reclassified as an Overlay visible-session loss.

### Hide while debounce is pending

Unsubmitted Overlay-local draft work may be discarded immediately.

OQ4 close must not wait for the debounce window.

An already-submitted Runtime mutation may settle normally; hidden/disposed UI ignores obsolete settlement according to its current-generation rule.

---

## 22. Shared-page inclusion rule

A page or feature enters the shared Quick Settings product model only when product policy intends parity between QAM and Overlay.

For included shared pages:

```text
same rows
same labels
same order
same control kinds
same value semantics
same mutation behavior
same commit policy
```

If a feature is intentionally QAM-only or Overlay-only, keep it outside the shared page until product policy changes.

Do not add a per-row surface-visibility matrix just to support hypothetical divergence.

The simplest model is:

> **inside shared page = parity**  
> **outside shared page = surface-specific**

Main UI remains separate regardless.

---

## 23. Current intended shared pages

### 23.1 Device

The initial shared Device page is:

```text
TDP Control
CPU Boost
Windows Power Mode
```

Center M authority remains Main-UI-only/reboot-bound and is not a Quick Settings row.

Future Fan Control / Battery Charge Limit are not placeholders. Add them only after their Runtime contracts exist and product explicitly approves QAM + Overlay parity.

### 23.2 Profile

Current QAM already has real Profile behavior based on `FrontendGameProfileSnapshot`.

The shared Profile page should initially mirror only the controls actually intended to be visible in QAM at the time its migration work order is prepared.

Current source includes profile mutation support for:

```text
Profile enable
TDP
CPU Boost
Windows Power Mode
Intel FPS Limit
Resolution data
```

but transport capability alone does not imply visible shared Quick Settings exposure.

For example, current `qam.js` contains an Intel FPS Limit path behind `SHOW_INTEL_FPS_LIMIT = false`; that hidden path must not become visible in both surfaces accidentally during generic migration.

Freeze the exact visible Profile row set in the focused Profile work order against then-current source.

---

## 24. Future feature extension rule

Once the generic QAM and Overlay renderers exist, adding a feature that uses an existing shared control kind should normally require only:

```text
1. real Runtime feature/typed contract exists
2. product approves QAM + Overlay parity
3. add row(s) to shared Quick Settings projection
4. add explicit central mutation mapping
5. add/adjust focused contract tests
```

No QAM renderer change.

No Overlay renderer change.

No duplicated label/order/debounce edit.

A renderer change is required only when a genuinely new shared control kind is introduced.

That is the primary maintenance goal of this architecture.

---

## 25. Explicit non-goals

Do not build:

- a generic settings database;
- JSON-authored dynamic forms;
- a plugin/provider UI system;
- a feature registry discovered by reflection;
- arbitrary string RPC dispatch;
- a cross-framework visual component abstraction;
- a universal Main UI/QAM/Overlay renderer;
- a new frontend process;
- a new hardware/state cache;
- a second Runtime feature authority;
- a page/layout designer;
- a formula/expression engine;
- generalized dependency graph;
- generic surface capability matrix;
- new lifecycle epochs/barriers/transactions;
- high-frequency feature polling;
- Full1902 controller lifecycle changes.

The shared model is intentionally a small closed list of known Quick Settings pages, sections, rows, control kinds, values, commit policies, and mutation intents.

---

## 26. Review standard

Block future Shared Quick Settings PRs for realistic defects such as:

- QAM and Overlay rendering different product rows from the same page snapshot;
- duplicate hard-coded product labels/order/policies remaining after migration;
- a debounce policy change still requiring edits in both surfaces;
- stale Profile context mutating the wrong AppId;
- invalid grouped TDP draft reaching Runtime;
- one feature failure discarding healthy shared rows;
- malformed mutation value shape reaching an unrelated Runtime method;
- QAM-specific admission leaking into Overlay or feature authority;
- Overlay Quick Settings traffic blocking OQ4 Hide/Dismiss processing;
- feature failure triggering controller teardown;
- transport disconnect leaving stale editable UI;
- real resource leaks/deadlocks in normal lifecycle.

Do not block for theoretical instruction-level races when current owner/gate/generation behavior converges safely in supported lifecycle.

Do not add state/locks/epochs/barriers solely because an artificial test can interleave operations at a single instruction boundary.

---

## 27. Final architecture target

```text
                              Addon Runtime
                                   │
                    one authority per real feature
                                   │
                     InProcessAddonFrontendControl
                                   │
           typed feature snapshots / typed mutations
                                   │
                    ┌──────────────┴──────────────┐
                    │                             │
                    │                  Shared Quick Settings
                    │                    product projection
                    │                             │
                    │          same pages / rows / labels / order
                    │          same values / options / constraints
                    │          same commit/debounce policy
                    │          same mutation intents
                    │                             │
                    │                ┌────────────┴────────────┐
                    │                │                         │
                    │              .Qam                    .Overlay
                    │                │                         │
                    ▼                ▼                         ▼
                 Main UI          QamHost                  Overlay.exe
              desktop-specific       │                         │
                   UI           Steam renderer             WinUI renderer
                                   │                         │
                             QAM admission              OQ4 admission
```

The final rule is:

> **Runtime owns truth. Shared Quick Settings owns the product definition for parity pages. QAM and Overlay own only how that same product definition is rendered and interacted with on their respective surfaces.**
