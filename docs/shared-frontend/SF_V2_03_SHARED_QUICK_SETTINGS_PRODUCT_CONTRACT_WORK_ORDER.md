# Work Order — SF-V2-03: Shared Quick Settings Product Contract + Device Projection / Dispatch

> **Date:** 2026-09-05  
> **Status:** Ready for implementation  
> **Track:** Shared Frontend V2 / Phase C  
> **Reviewed repository head:** `main` at `c16442747226a32cc14330e0af0fe84487149f38`  
> **Reviewed production baseline:** PR #498 merge at `ed27976ff756ecb5bfc42569d642acb413b452a9`  
> **Previous phases:** SF-V2-01 / PR #496 and SF-V2-02 / PR #498 are complete  
> **Architecture authority:** `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md`  
> **PR roadmap:** `docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md`

---

## 1. Goal

Create the single **shared Quick Settings product-definition contract** that Steam QAM and the Addon Overlay will later render with different UI technologies.

This PR does **not** change either visible surface yet.

It establishes one typed, closed, stateless definition of:

```text
what Device Quick Settings rows exist
which order they appear in
what their labels are
whether each row is Toggle or Slider
what current value/range/options are shown
whether a row is available/writable
which mutation intent a row represents
which commit policy a row uses
which sliders share one pending commit group
which TDP sliders have linked PL1/PL2 constraints
```

Target architecture after this PR:

```text
CpuBoostRuntime / TdpRuntime / PowerModeRuntime
                    ↓
       InProcessAddonFrontendControl
                    ↓
 FrontendDeviceQuickSettingsSnapshot
                    ↓
      stateless Quick Settings projection
                    ↓
        QuickSettingsPageSnapshot(Device)
                    ↓
      future surface-specific transports
             ↙                 ↘
       Steam QAM             Overlay
       renderer              renderer
```

Mutation direction:

```text
future QAM / Overlay renderer
        ↓
QuickSettingsMutationIntent
        ↓
one explicit Quick Settings adapter
        ↓
existing typed IAddonFrontendControl mutation methods
        ↓
existing Runtime / persistence / hardware authority
        ↓
fresh QuickSettingsPageSnapshot
```

The fundamental rule is:

> **Share the Quick Settings product semantics, not the renderer or lifecycle authority.**

---

## 2. Why this PR exists now

SF-V2-01 and SF-V2-02 deliberately built the data/transport foundations first.

Current code now has:

```text
one FrontendDeviceQuickSettingsSnapshot
one CaptureDeviceQuickSettingsAsync authority projection
Main UI aggregate Device refresh
QAM aggregate Device refresh
Overlay Device transport v6
```

But QAM still hard-codes Device product behavior in `qam.js`, including:

```text
QAM_SLIDER_COMMIT_DELAY_MS = 2000
Device labels and row order
CPU Boost option labels/order
Windows Power Mode option labels/order
TDP local PL1/PL2 gap policy
feature-specific mutation method names
feature-specific pending-draft keys
```

Overlay separately has:

```text
OverlayDelayedSliderCommit.ProductionDelay = 2000 ms
```

and still only contains preview Device rows.

If real Overlay Device binding starts from this point without a shared product layer, every future product change would need to be implemented independently in JavaScript and C#.

SF-V2-03 freezes the shared product contract **before** that duplication becomes production UI.

---

## 3. Latest-baseline verification

This work order was prepared after re-checking current `main`.

Current repository head:

```text
c16442747226a32cc14330e0af0fe84487149f38
```

That head contains only the current Shared Frontend architecture/roadmap documentation on top of the production baseline from PR #498.

Current production protocol versions are:

```text
FrontendTransportProtocol.CurrentVersion = 27
OverlayTransportProtocol.CurrentVersion  = 6
```

Current source facts relevant to this PR:

```text
FrontendDeviceQuickSettingsSnapshot exists
IAddonFrontendControl.CaptureDeviceQuickSettingsAsync exists
focused CPU/TDP/Power capture + mutation methods remain the authority
Overlay v6 still uses OverlayDeviceMutationKind / OverlayDeviceMutationDispatch
QAM still uses feature-specific Device bridge methods
Overlay still uses preview rows, not real Device bindings
```

SF-V2-03 changes **neither wire protocol**.

Required result after this PR:

```text
FrontendTransportProtocol.CurrentVersion == 27
OverlayTransportProtocol.CurrentVersion  == 6
```

If `main` moves before implementation begins, re-check the exact current versions and preserve them. Do not force these historical numbers if another unrelated PR has legitimately moved them first.

---

## 4. Required reading before implementation

Read the current versions of all of the following. Do not implement this work from the conceptual type names in the roadmap alone.

### 4.1 Full PID1902 authority

Read in the precedence defined by the Full1902 README:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
```

Frozen invariant:

```text
Runtime = controller / feature / hardware / persistence authority
frontend surfaces = disposable presentation clients
```

This PR must not alter controller authority or lifecycle.

### 4.2 Shared Frontend authority

Read:

```text
docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
docs/shared-frontend/SF_V2_01_DEVICE_QUICK_SETTINGS_SHARED_AGGREGATE_WORK_ORDER.md
docs/shared-frontend/SF_V2_02_OVERLAY_DEVICE_QUICK_SETTINGS_TRANSPORT_WORK_ORDER.md
```

The current architecture document supersedes the earlier assumption that QAM and Overlay should independently own Device/Profile presentation policy.

### 4.3 Overlay lifecycle / debounce reference

Read:

```text
docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md
docs/overlayui/OQ5_UI_07_SHARED_DELAYED_SLIDER_COMMIT_WORK_ORDER.md
```

SF-V2-03 must not change OQ4 capture, neutral publication, release-gating, Show/Hide, or Overlay process lifetime.

### 4.4 Current source

At minimum inspect:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.Contracts/DeviceProfiles/CpuBoostMode.cs
src/SteamInputAddonforClaw.Contracts/DeviceProfiles/WindowsPowerMode.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw/Frontend/FrontendSnapshotMapper.cs
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw.Overlay/OverlayDelayedSliderCommit.cs
```

Also inspect the existing CPU Boost / TDP / Power Mode frontend tests and `DeviceQuickSettingsAggregateTests.cs` before adding new tests.

---

## 5. Scope boundary

SF-V2-03 is **Runtime/frontend-contract foundation only**.

### In scope

```text
closed typed Quick Settings contracts
Device page projection
Device presentation policy centralization
Device mutation-intent validation
Device mutation dispatch onto existing typed frontend methods
fresh authoritative Device page re-projection after mutation
focused tests
```

### Not in scope

```text
QAM renderer migration
Overlay renderer migration
QAM bridge generic RPC
Frontend named-pipe generic RPC
Overlay v7 generic Quick Settings wire
Profile page projection/mutation
Main UI migration
new hardware/persistence logic
```

The visible QAM and Overlay behavior must remain unchanged in this PR.

---

## 6. File / dependency placement

Do not create a new project or service for this work.

The current contracts project uses SDK default compile item discovery, so a new `.cs` file is sufficient.

Preferred placement:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/
    QuickSettingsContracts.cs            new

src/SteamInputAddonforClaw/Frontend/
    QuickSettingsPresentation.cs         new, or equivalent
    QuickSettingsMutationAdapter.cs      new, or equivalent
    InProcessAddonFrontendControl.cs     small explicit seam wiring

tests/SteamInputAddonforClaw.Tests/
    QuickSettingsPresentationTests.cs    new, or equivalent
    QuickSettingsMutationAdapterTests.cs new, or equivalent
```

Exact file names may differ if a clearer split results.

Do **not** put a large new contract family into `FrontendContracts.cs` if a separate adjacent contract file keeps ownership clearer.

Do not add:

```text
QuickSettingsManager
QuickSettingsRuntime
QuickSettingsService
QuickSettingsRegistry
QuickSettingsStore
QuickSettingsViewModel framework
new DI registration hierarchy
```

The projection and mutation adapter must be stateless.

---

## 7. Closed shared contract vocabulary

The contract must be public from the Contracts assembly because later QAM and Overlay transports will serialize the same typed model.

However, the vocabulary is deliberately **closed**.

Do not use arbitrary feature-name strings, `JsonElement`, reflection, or plugin-style descriptors as the product contract.

### 7.1 Page identity

Initial page IDs:

```csharp
public enum QuickSettingsPageId
{
    Device,
    Profile,
}
```

Only Device is projected in SF-V2-03.

Profile is reserved because it is the next approved parity page, but its projection/mutation remains out of scope for this PR.

Do not pre-add:

```text
Controller
Shortcut
Setting
```

merely because the Overlay has those tabs.

### 7.2 Section identity

Required Device sections:

```text
DeviceTdp
DeviceCpuBoost
DevicePowerMode
```

Reserve only the currently visible Profile parity grouping needed by the later Profile milestone:

```text
ProfileGeneral
ProfileTdp
ProfileCpuBoost
ProfilePowerMode
```

Do not reserve Intel FPS Limit or Resolution sections in this PR.

Current `qam.js` keeps Intel FPS Limit behind:

```text
SHOW_INTEL_FPS_LIMIT = false
```

and does not currently render Resolution in the QAM page. Existing backend capability does not imply visible shared Quick Settings exposure.

### 7.3 Row identity

Required Device row IDs:

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

Reserve these current Profile parity identities only:

```text
ProfileEnabled

ProfileTdpEnabled
ProfileTdpAcPl1
ProfileTdpAcPl2
ProfileTdpDcPl1
ProfileTdpDcPl2

ProfileCpuBoostEnabled
ProfileCpuBoostAc
ProfileCpuBoostDc

ProfilePowerModeEnabled
ProfilePowerModeAc
ProfilePowerModeDc
```

Again: these Profile IDs are vocabulary reservation only. SF-V2-03 must not implement Profile projection or mutations.

Do not add future/hidden rows such as:

```text
ProfileFpsLimit*
ProfileResolution
FanControl*
BatteryChargeLimit*
Controller*
```

### 7.4 Control kinds

Support only:

```text
Toggle
Slider
```

Slider kinds:

```text
Numeric
Discrete
```

This is enough for all current Device Quick Settings.

Do not add a generic Choice/Action/Color/Form/Editor control family in advance.

---

## 8. Value contract — closed and strictly typed

Current shared controls require only Boolean and integral/discrete values.

Use a small closed value shape, conceptually:

```text
QuickSettingsValueKind
├─ Boolean
└─ Integer
```

A practical record may carry a discriminant plus nullable value fields, for example:

```csharp
public sealed record QuickSettingsValue(
    QuickSettingsValueKind Kind,
    bool? BooleanValue = null,
    int? IntegerValue = null);
```

Exact shape may differ, but validation must prove the value is structurally valid for its declared kind.

Required rule:

```text
Boolean → exactly BooleanValue populated
Integer → exactly IntegerValue populated
```

Do not use:

```text
object
Dictionary<string, object>
JsonElement
string method name + arbitrary payload
```

as the shared product contract.

---

## 9. Page / section / row snapshot shape

The exact C# records may vary, but the following semantics are required.

### 9.1 Page

`QuickSettingsPageSnapshot` must carry at least:

```text
PageId
optional context identity (Profile later uses AppId; Device has none)
availability/message as needed
ordered Sections
linked slider constraints
```

A simple optional `uint? AppId` is sufficient for the initial context identity if that keeps the contract clear. Do not add a generic context dictionary.

Provide an explicit unavailable shape/factory so unsupported pages can fail closed without fabricating usable controls.

### 9.2 Section

Each section carries:

```text
SectionId
optional product label
ordered Rows
optional product message if actually needed
```

The section identity/order is semantic. QAM may later render Steam `PanelSection`; Overlay may render low-chrome grouped rows.

### 9.3 Row

Each row must carry enough product information that a future renderer does not need feature-specific knowledge:

```text
RowId
Label
ControlKind
Available
Writable
Value
SliderSpec when applicable
CommitPolicy
CommitGroupId when applicable
```

Do not embed surface-specific facts such as:

```text
QAM Big Picture admission
Overlay Visible/OQ4-captured admission
React component type
WinUI control instance
busy spinner state
controller focus state
```

Those remain renderer/surface concerns.

---

## 10. Shared slider specification

A slider spec must represent either a numeric range or a discrete ordered set.

### Numeric slider

Required data:

```text
Minimum
Maximum
Step
optional value suffix/display unit
```

Current TDP uses integer watts with:

```text
Step = 1
```

A suffix such as `W` may be product data if used by both surfaces; do not hard-code renderer-specific formatting rules into this PR unless current shared design needs it.

### Discrete slider

Required data:

```text
ordered options[]
  Value
  Label
```

The renderer must receive option ordering and labels from the shared model rather than reconstructing them from enum names.

---

## 11. Shared commit policy

Commit timing is product behavior.

Represent it in the row contract.

Required modes:

```text
Immediate
TrailingDebounce
```

Current product values:

```text
Toggle → Immediate
Slider → TrailingDebounce(2000 ms)
```

Conceptually:

```csharp
public enum QuickSettingsCommitMode
{
    Immediate,
    TrailingDebounce,
}

public sealed record QuickSettingsCommitPolicy(
    QuickSettingsCommitMode Mode,
    int DelayMilliseconds);
```

Validation:

```text
Immediate         → DelayMilliseconds == 0
TrailingDebounce  → DelayMilliseconds > 0
```

Define/reuse one shared 2000 ms slider policy in the projection layer rather than repeating the literal per row.

### Important transition rule

Do **not** remove these current surface constants yet:

```text
QAM_SLIDER_COMMIT_DELAY_MS
OverlayDelayedSliderCommit.ProductionDelay
```

Neither surface consumes the new page contract in SF-V2-03.

Those duplicate constants are removed in the QAM/Overlay migration PRs after the shared contract becomes reachable over each transport.

---

## 12. Commit groups

A commit group represents sliders that must share one pending draft / one trailing commit.

Do not create a commit-group enum entry for every independent slider.

Preferred rule:

```text
CommitGroupId == null
→ independent row; renderer may use RowId as its local pending key

CommitGroupId != null
→ multiple rows participate in one whole-group draft/commit
```

Initial required group:

```text
DeviceTdpConfiguration
```

Reserve for later Profile parity only:

```text
ProfileTdpConfiguration
```

All four Device TDP numeric sliders use:

```text
CommitGroupId = DeviceTdpConfiguration
```

The TDP Enable toggle remains `Immediate` and is not itself a delayed slider group member.

---

## 13. Linked slider constraints — TDP only

Current QAM contains a TDP-specific PL1/PL2 local correction rule.

Move the **product policy data** into the shared projection so QAM and Overlay later apply the same rule.

Use one narrow typed constraint shape, conceptually:

```text
LowerRowId
UpperRowId
MinimumGap
```

Do not build:

```text
expression trees
formula engine
validation DSL
arbitrary dependency graph
constraint registry
```

### 13.1 Current proven gap policy

From current QAM behavior:

```text
PL1 min/max = 8 / 30
PL2 min/max = 8 / 37
→ minimum PL1→PL2 gap = 1 W

PL1 min/max = 8 / 35
PL2 min/max = 8 / 45
→ minimum PL1→PL2 gap = 2 W

other limit shape
→ no shared local gap correction policy yet
```

When a positive gap is recognized, emit two constraints:

```text
DeviceTdpAcPl1 → DeviceTdpAcPl2
DeviceTdpDcPl1 → DeviceTdpDcPl2
```

with the same model-derived minimum gap.

For an unknown/other limit shape, emit no linked constraint. The existing typed TDP Runtime remains the final validity authority.

Do not invent new model policies from range arithmetic alone.

---

## 14. Device page projection — one source of product content

Add one small stateless projection adjacent to the existing frontend mapper layer.

Conceptually:

```csharp
QuickSettingsPageSnapshot BuildDevice(
    FrontendDeviceQuickSettingsSnapshot snapshot)
```

It must not:

```text
read hardware
persist anything
mutate Runtime
subscribe to events
cache state
raise StateInvalidated
own a timer
```

It is a pure mapping from already-captured typed truth to product rows.

---

## 15. Frozen Device section / row order

The shared Device page order is:

```text
1. TDP
2. CPU Boost
3. Windows Power Mode
```

### TDP section

When only the parent is visible:

```text
TDP Control                    Toggle
```

When TDP is enabled and a valid configuration/limits snapshot exists:

```text
TDP Control                    Toggle
Plugged in · PL1              Numeric Slider
Plugged in · PL2              Numeric Slider
On battery · PL1              Numeric Slider
On battery · PL2              Numeric Slider
```

### CPU Boost section

```text
CPU Boost                     Toggle
```

When enabled, append:

```text
Plugged in                    Discrete Slider
On battery                    Discrete Slider
```

### Windows Power Mode section

```text
Windows Power Mode            Toggle
```

When enabled, append:

```text
Plugged in                    Discrete Slider
On battery                    Discrete Slider
```

Do not add a conditional-visibility expression language. The projection simply emits the rows that should currently be visible.

This guarantees both later renderers receive the same visible product rows and order.

---

## 16. CPU Boost projection semantics

Use the existing `FrontendCpuBoostSnapshot` only.

### 16.1 Discrete options — exact order and labels

Source enum values are already fixed from 0 through 6. The shared presentation labels must be:

```text
0  Disabled
1  Enabled
2  Aggressive
3  Efficient Enabled
4  Efficient Aggressive
5  Aggressive At Guaranteed
6  Efficient Aggressive At Guaranteed
```

Do not derive the display labels by splitting enum member names in each renderer.

The shared row owns these labels/order.

### 16.2 Current displayed value

For each AC/DC side:

```text
Desired != null
→ show Desired

else CurrentStatus == Known
→ show Current

else
→ no safe slider value; row is unavailable/non-writable
```

Do not fabricate `Disabled` merely because no value can be read.

### 16.3 Availability / writability

Availability means the shared product can represent meaningful state.

Writability is feature-local only and must derive from existing snapshot/persistence semantics.

At minimum preserve:

```text
PersistenceWritable == false
→ no CPU Boost mutation row is writable

Enabled == false
→ AC/DC child sliders are not emitted
```

Do not include QAM/Overlay admission or transient busy state in `Writable`.

The surface later combines:

```text
row.Writable
AND surface admission
AND !surface-local busy state
```

before accepting a mutation.

---

## 17. Windows Power Mode projection semantics

Use only `FrontendPowerModeSnapshot` and the existing `WindowsPowerMode` enum.

### 17.1 Exact discrete options

```text
0  Best power efficiency
1  Balanced
2  Best performance
```

Do not make the renderers own these labels.

### 17.2 Current displayed value

For each side:

```text
Desired != null
→ show Desired

else CurrentStatus == Known
→ show Current

else
→ no safe slider value
```

### 17.3 Writability

Preserve the current effective QAM initialization rule at the shared feature level:

```text
PersistenceWritable
AND AC Desired is initialized
AND DC Desired is initialized
→ Device Power Mode mutations may be writable
```

Surface-specific admission/busy conditions remain outside the shared contract.

When `Enabled == false`, the AC/DC child rows are not emitted.

---

## 18. TDP projection semantics

Use only:

```text
FrontendTdpSnapshot.Available
FrontendTdpSnapshot.PersistenceWritable
FrontendTdpSnapshot.Configuration
FrontendTdpSnapshot.Limits
```

### 18.1 Toggle

The `TDP Control` row is writable only when the existing feature snapshot permits safe persistence/mutation.

An unavailable TDP child must not make CPU Boost or Power Mode unavailable.

### 18.2 Numeric slider limits

Use the Runtime-provided semantic limits directly:

```text
PL1:
  min = Pl1MinimumWatts
  max = Pl1MaximumWatts
  step = 1

PL2:
  min = Pl2MinimumWatts
  max = Pl2MaximumWatts
  step = 1
```

Current QAM's `SliderField` visually uses PL2 max for PL1 but separately caps the PL1 value to `Pl1MaximumWatts` before commit. Do **not** preserve that renderer artifact as shared product semantics.

The effective product constraint has always been `Pl1MaximumWatts`; expose that exact semantic maximum to both future renderers.

This changes no valid value range accepted by the product.

### 18.3 Whole configuration draft

All four numeric rows are projections of one:

```text
FrontendTdpConfiguration
├─ Enabled
├─ AC  (PL1, PL2)
└─ DC  (PL1, PL2)
```

They share `DeviceTdpConfiguration` as the commit group.

If TDP is enabled but the configuration or limits are missing/invalid, do not fabricate slider values/ranges. Leave the feature safely unavailable/non-writable for slider editing.

---

## 19. Feature-local failure isolation

`FrontendDeviceQuickSettingsSnapshot` already preserves sibling state when one child feature is unavailable.

The shared page projection must preserve that property.

Examples:

```text
TDP unavailable
→ TDP rows disabled/unavailable
→ CPU Boost + Power Mode remain independently usable

CPU Boost unavailable
→ CPU Boost rows disabled/unavailable
→ TDP + Power Mode unaffected

Power Mode unavailable
→ Power rows disabled/unavailable
→ CPU Boost + TDP unaffected
```

Do not collapse one child failure into a page-wide unavailable state unless the page itself cannot be projected at all.

---

## 20. Shared mutation intent

Add one closed mutation-intent contract.

Required semantics:

```text
PageId
optional context AppId
EditedRowId
Values[]
    RowId
    typed QuickSettingsValue
```

For an independent Toggle/Slider, `Values` contains exactly the edited row.

For the grouped Device TDP slider commit, `Values` contains the entire current TDP draft required to reconstruct one `FrontendTdpConfiguration`.

Do not add a string method name.

Do not put transport request IDs into this product contract; each transport owns correlation separately.

---

## 21. Shared mutation result

The generic product result should be renderer-agnostic:

```text
Succeeded
FailureMessage
Page = fresh authoritative QuickSettingsPageSnapshot
```

Conceptually:

```csharp
public sealed record QuickSettingsMutationResult(
    bool Succeeded,
    string? FailureMessage,
    QuickSettingsPageSnapshot Page);
```

Do not copy every feature-specific mutation outcome enum into another generic enum hierarchy.

The underlying typed mutation still retains its exact outcome semantics. The adapter converts that operation into a generic Quick Settings result and then reprojects current truth.

This is particularly important for existing `ApplyFailed` behavior: a failed Windows apply may still have successfully persisted a new desired value. The returned fresh page must reflect Runtime truth rather than rolling the UI back based on a generic Boolean assumption.

---

## 22. `IAddonFrontendControl` seam

Add the smallest explicit in-process API needed for the future generic transports.

Preferred shape, conceptually:

```csharp
Task<QuickSettingsPageSnapshot> CaptureQuickSettingsPageAsync(
    QuickSettingsPageId pageId,
    uint? appId = null,
    CancellationToken cancellationToken = default);

Task<QuickSettingsMutationResult> MutateQuickSettingAsync(
    QuickSettingsMutationIntent intent,
    CancellationToken cancellationToken = default);
```

A small typed context record is acceptable instead of `uint? appId` if it is clearly simpler, but do not introduce a generic property bag.

### Default interface fallback

Because `IAddonFrontendControl` has many test/passive implementations and current default methods intentionally fail closed, provide safe default behavior for these new methods rather than forcing unrelated test doubles to implement them immediately.

Conceptually:

```text
Capture unsupported page
→ QuickSettingsPageSnapshot.Unavailable(page/context)

Mutate unsupported page/row
→ Succeeded=false
→ failure message
→ unavailable page
```

Do not let a default implementation fabricate a successful mutation.

---

## 23. In-process page capture

Implement the production seam in `InProcessAddonFrontendControl`.

### Device

```text
CaptureQuickSettingsPageAsync(Device)
→ existing CaptureDeviceQuickSettingsAsync
→ stateless BuildDevice(...)
→ return page
```

Do not independently read CPU/TDP/Power again inside the projection.

### Profile

SF-V2-03 does not implement Profile projection.

Required behavior:

```text
CaptureQuickSettingsPageAsync(Profile, ...)
→ explicit unavailable Profile page
→ zero profile mutation / zero new scan side effect
```

Profile becomes real in SF-V2-08.

### Unknown/invalid page

Fail closed. Do not choose Device as a fallback.

---

## 24. Central Device mutation adapter

Add one explicit adapter in the Runtime/frontend project.

It may accept `IAddonFrontendControl` so it can call the **existing typed frontend methods** and remain focused/testable.

Conceptually:

```text
QuickSettingsMutationIntent
        ↓
validate page / row / value shape
        ↓
explicit switch
        ↓
existing typed IAddonFrontendControl mutation
        ↓
CaptureDeviceQuickSettingsAsync
        ↓
BuildDevice
        ↓
QuickSettingsMutationResult
```

Do not call CpuBoostRuntime/TdpRuntime/PowerModeRuntime directly from the adapter.

Do not duplicate ProfileStore/hardware apply logic.

---

## 25. Exact Device mutation mapping

The adapter must map only these currently approved Device intents.

```text
DeviceCpuBoostEnabled
→ SetDeviceCpuBoostEnabledAsync

DeviceCpuBoostAc
→ SetDeviceCpuBoostAcAsync

DeviceCpuBoostDc
→ SetDeviceCpuBoostDcAsync

DeviceTdpEnabled
→ SetDeviceTdpEnabledAsync

Device TDP slider group
→ SetDeviceTdpAsync

DevicePowerModeEnabled
→ SetDevicePowerModeEnabledAsync

DevicePowerModeAc
→ SetDevicePowerModeAcAsync

DevicePowerModeDc
→ SetDevicePowerModeDcAsync
```

No other row is mutable in SF-V2-03.

Do not create a generic `InvokeSetting` / reflection dispatcher.

---

## 26. Mutation validation — independent rows

Validation occurs before any typed mutation method is called.

Malformed intents must invoke **zero** mutations.

### 26.1 Device page context

Device mutation requires:

```text
PageId == Device
AppId == null / absent
```

A Device intent carrying a game AppId is invalid.

A Profile intent is unavailable in this PR and must invoke zero Device mutations.

### 26.2 Toggle rows

These rows require exactly one Boolean value for the same row ID:

```text
DeviceTdpEnabled
DeviceCpuBoostEnabled
DevicePowerModeEnabled
```

No extras, duplicates, or mismatched row IDs.

### 26.3 CPU Boost discrete rows

These rows require exactly one Integer value matching a defined `CpuBoostMode` member:

```text
DeviceCpuBoostAc
DeviceCpuBoostDc
```

Do not accept an arbitrary integer merely because C# can cast it to the enum.

Allowed values are the currently defined `0..6` members only.

### 26.4 Power Mode discrete rows

These rows require exactly one Integer value matching a defined `WindowsPowerMode` member:

```text
DevicePowerModeAc
DevicePowerModeDc
```

Allowed values are the three defined enum members only.

---

## 27. Mutation validation — grouped TDP sliders

A TDP slider edit is not an independent single-value mutation.

The intent's `EditedRowId` must be exactly one of:

```text
DeviceTdpAcPl1
DeviceTdpAcPl2
DeviceTdpDcPl1
DeviceTdpDcPl2
```

Its `Values` collection must contain **exactly once each**:

```text
DeviceTdpEnabled     Boolean
DeviceTdpAcPl1       Integer
DeviceTdpAcPl2       Integer
DeviceTdpDcPl1       Integer
DeviceTdpDcPl2       Integer
```

Requirements:

```text
no duplicates
no missing members
no unrelated row values
Enabled == true for a slider-group commit
all numeric values are integral typed values
```

Construct exactly one:

```csharp
FrontendTdpConfiguration
```

and invoke:

```text
SetDeviceTdpAsync(configuration)
```

exactly once.

### Do not duplicate final TDP authority validation

The generic adapter must not reimplement all `TdpRuntime` model/range/hardware validation.

The shared product projection provides renderer range/linked-draft policy.

The existing typed TDP mutation remains the final validity/operation authority and can still return `InvalidTarget`, `PersistenceFailed`, or `Unavailable`.

---

## 28. Authoritative re-projection after every valid mutation attempt

After an underlying typed mutation returns — whether it reports success or a normal typed feature failure — return a freshly projected Device page.

Required sequence:

```text
validated Quick Settings mutation
→ invoke one existing typed operation
→ receive typed result
→ CaptureDeviceQuickSettingsAsync
→ BuildDevice(current aggregate)
→ return generic result + fresh page
```

Do not simply patch the submitted value into the old page.

Do not assume a failed mutation means the old value is authoritative.

The Runtime snapshot is authority.

### Invalid intent

For a structurally invalid/mismatched intent:

```text
invoke zero typed mutation methods
→ return Succeeded=false
→ include a concise validation failure
→ return current fresh Device page when it can be captured safely
```

If the page itself cannot be captured under existing shutdown/cancellation semantics, preserve the existing exception/cancellation behavior rather than fabricating healthy state.

---

## 29. Cancellation / shutdown / exception behavior

Do not add a new exception policy.

Preserve current frontend conventions.

### Cancellation

A caller cancellation must remain cancellation.

Do not convert `OperationCanceledException` into a normal `Succeeded=false` product result.

### Process shutdown

Existing `InProcessAddonFrontendControl` shutdown barriers remain authoritative.

Quick Settings capture/mutation must not bypass `ThrowIfShuttingDown()` behavior already enforced by underlying operations.

### Unexpected operation exception

Do not swallow unexpected Runtime/programming/transport-adjacent exceptions inside the pure product adapter merely to make a generic result.

Later transport layers own conversion to their wire error semantics.

---

## 30. `StateInvalidated` ownership

Do not add a second invalidation event or manually raise duplicate invalidations from the generic adapter.

Existing typed mutation methods already own the correct Runtime/frontend invalidation behavior.

Required rule:

```text
generic adapter
→ calls existing typed mutation
→ does NOT add another StateInvalidated notification merely because it wrapped the call
```

Quick Settings page capture remains read-only and must not raise invalidation.

Do not add:

```text
QuickSettingsChanged event
per-row invalidation event
revision counter
feature dirty bitmask
```

---

## 31. Surface admission is explicitly NOT part of this PR

The shared `Writable` fact means only that the **feature state/persistence contract** allows a mutation.

It must not encode QAM or Overlay admission.

### QAM remains later-surface policy

Current QAM rule remains:

```text
Big Picture active
AND AppId == 0
→ Device mutation may be admitted
```

### Overlay remains later-surface policy

Current OQ4/SF-V2-02 rule remains:

```text
Overlay Ready
AND Visible
AND _overlayCaptureActive
AND not shutting down
→ Device mutation may be admitted
```

Do not move either rule into:

```text
QuickSettingsPresentation
QuickSettingsMutationIntent validation
CpuBoostRuntime
TdpRuntime
PowerModeRuntime
```

The shared adapter validates **what the requested product action means**, not **which frontend is currently allowed to ask for it**.

---

## 32. Keep current transports untouched

SF-V2-03 does not add wire operations.

Do not edit merely for this PR:

```text
FrontendRpcMethod
FrontendWireCodec
NamedPipeAddonFrontendServer
NamedPipeAddonFrontendClient
QamFrontendBridge
OverlayWireMessageKind
OverlayDeviceMutationKind
OverlayDeviceMutationDispatch
NamedPipeOverlayServer
NamedPipeOverlayClient
```

The current v6 Overlay Device-specific path temporarily remains alongside the new in-process product adapter.

This is intentional transition state.

It is replaced/superseded later in SF-V2-06 before real Overlay Device UI consumes the generic model.

Do not try to collapse SF-V2-03 through SF-V2-06 into one PR.

---

## 33. Keep current visible UI untouched

Do not edit:

```text
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlaySliderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayToggleRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayDelayedSliderCommit.cs
```

for SF-V2-03 unless a compile-only namespace/reference change is genuinely required — which current dependency layout should not require.

Specifically leave:

```text
QAM_SLIDER_COMMIT_DELAY_MS = 2000
OverlayDelayedSliderCommit.ProductionDelay = 2000 ms
```

as-is until their renderers migrate to the shared page contract.

This PR must not produce a user-visible QAM/Overlay change.

---

## 34. Main UI remains independent

Do not migrate `DevicePage.xaml.cs` to `CaptureQuickSettingsPageAsync`.

The Main UI already correctly consumes:

```text
FrontendDeviceQuickSettingsSnapshot
```

for its desktop-specific Device page.

Shared Quick Settings is specifically the QAM + Overlay compact product model.

Main UI remains the superset management UI with its own layout and workflows, including Center M authority.

---

## 35. Required tests — contract / projection

Add focused deterministic tests.

At minimum verify all of the following.

### A. Exact Device section order

```text
DeviceTdp
DeviceCpuBoost
DevicePowerMode
```

### B. Exact Device row order / labels

Verify the enabled-state layouts exactly match the shared product order from section 15.

### C. CPU Boost discrete options

Verify the seven values and labels exactly, in order.

### D. Power Mode discrete options

Verify the three values and labels exactly, in order.

### E. Commit policy

```text
all Toggles → Immediate / 0 ms
all Device Sliders → TrailingDebounce / 2000 ms
```

### F. TDP commit group

All four TDP numeric rows share exactly:

```text
DeviceTdpConfiguration
```

CPU/Power sliders do not accidentally join that group.

### G. TDP numeric ranges

Given Runtime limits, verify:

```text
PL1 min/max = Pl1Minimum/Pl1Maximum
PL2 min/max = Pl2Minimum/Pl2Maximum
step = 1
```

### H. Known TDP gap policies

Verify:

```text
8/30/8/37 → 1 W AC + DC constraints
8/35/8/45 → 2 W AC + DC constraints
other shape → no inferred linked constraint
```

### I. Parent enabled visibility

Verify child sliders are emitted only when their parent feature is enabled and has enough safe state to represent the sliders.

### J. Partial child failure isolation

Unavailable TDP must not disable CPU/Power rows, etc.

### K. Desired/current preference

For CPU Boost and Power Mode verify:

```text
Desired wins when present
known Current is fallback when Desired absent
unknown/unavailable state is never fabricated into a valid slider value
```

---

## 36. Required tests — mutation adapter

### A. Exact dispatch — one call only

For every supported Device independent row, prove the intent reaches exactly one corresponding existing typed method.

### B. CPU Boost enum validation

Defined values succeed in validation.

Undefined integer value invokes zero mutation methods.

### C. Power Mode enum validation

Defined values succeed in validation.

Undefined integer value invokes zero mutation methods.

### D. Toggle shape validation

Wrong value kind / duplicate / extra / mismatched row invokes zero mutation methods.

### E. TDP complete-group requirement

A valid whole group:

```text
Enabled
AC PL1
AC PL2
DC PL1
DC PL2
```

constructs exactly one `FrontendTdpConfiguration` and invokes `SetDeviceTdpAsync` exactly once.

Missing/duplicate/extra/wrong-type TDP values invoke zero mutations.

### F. Device context validation

A Device intent with AppId/context target is rejected with zero mutation.

### G. Profile remains unimplemented

Profile capture returns explicit unavailable state.

Profile mutation invokes zero Device mutation methods.

### H. Authoritative result re-projection

For typed mutation success and typed mutation failure, verify the generic result page comes from a fresh post-operation Device capture rather than from the submitted draft.

Include at least one apply/persistence failure case where returned typed snapshot/current Runtime truth differs from the attempted value.

### I. No duplicate invalidation authority

The generic wrapper must not create a second invalidation event beyond the underlying typed operation.

### J. Read-only page capture

`CaptureQuickSettingsPageAsync(Device)` must not persist/apply anything merely because it was called.

---

## 37. Regression tests to preserve

Do not weaken/delete current assertions merely to land the new generic contract.

Keep passing at minimum:

```text
DeviceQuickSettingsAggregateTests
CpuBoost frontend tests
TDP frontend contract tests
Power Mode frontend tests
QamFrontendContractTests
FrontendNamedPipeTransportTests
OverlayDeviceQuickSettingsTransportTests
OverlayDelayedSliderCommitTests
OQ4 / Overlay capture/session tests
UI architecture tests
```

The visible QAM source contract tests should still observe the old feature-specific implementation after SF-V2-03 because QAM migration has not happened yet.

---

## 38. Protocol/version assertions

Because no wire changes occur:

```text
FrontendTransportProtocol.CurrentVersion
→ unchanged from implementation baseline

OverlayTransportProtocol.CurrentVersion
→ unchanged from implementation baseline
```

On the reviewed baseline that means:

```text
27
6
```

Add/adjust a focused assertion only if useful; do not churn unrelated historical version comments.

---

## 39. No Full1902 / OQ4 lifecycle changes

This PR must not modify behavior in:

```text
Center M Enabled/Disabled authority
PID1901 / PID1902 transitions
DirectInput ownership
HidHide baseline / normalization
VIIPER ownership / teardown
Xbox360 / SteamDeck presentation selection
physical device loss / PnP re-enumeration
sleep / hibernate / resume
restart / shutdown teardown
OQ4 capture / neutral publication
OQ4 consumed-control release gate
Overlay Show/Hide retirement
front-button mapping / WING / OEM1 handling
```

If implementation appears to require touching any of these areas, stop and reassess the design. A stateless frontend product projection should not need controller-lifecycle changes.

---

## 40. Overengineering guardrails

Do not add machinery for hypothetical future UI requirements.

Forbidden unless a real current requirement proves otherwise:

```text
generic feature registry
capability registry
surface visibility matrix
reflection-based mutation dispatch
plugin/provider framework
schema-driven form engine
arbitrary validation DSL
expression tree / formula engine
global Quick Settings state cache
persistent Quick Settings metadata
new process / service
new named pipe
new manager hierarchy
global mutation scheduler
cross-feature transaction/lock/epoch/barrier
revision vector / per-row versioning
```

A handful of explicit enums + records + one pure projection + one explicit switch/adapter is the intended solution.

---

## 41. Expected diff shape

Expected focused production change is approximately:

```text
Contracts:
  + Quick Settings typed records/enums

Runtime/frontend:
  + Device product projection
  + Device mutation adapter
  + two small IAddonFrontendControl / InProcess seams

Tests:
  + product projection tests
  + mutation adapter tests
```

Expected untouched product areas:

```text
QAM JavaScript
Overlay UI
Frontend transport protocol
Overlay transport protocol
Main UI pages
Controller lifecycle
hardware implementations
```

If the PR grows into renderer/transport migration as well, split it. Those are already planned as later SF-V2 PRs.

---

## 42. Build / verification

During implementation run focused tests as needed, then complete at minimum:

```powershell
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

No new warnings.

CI must remain compatible with the existing Windows `Build and Test` workflow.

---

## 43. Acceptance criteria

SF-V2-03 is complete only when all of the following are true:

```text
1. A closed typed Quick Settings product contract exists in Contracts.

2. Device product content is generated from one stateless Runtime-side projection.

3. Device section order, row order, labels, control kinds, options, ranges,
   commit policy, TDP group, and linked constraints are represented by that
   shared page — not by a new renderer-specific definition.

4. Slider policy is represented centrally as TrailingDebounce(2000 ms), while
   current QAM/Overlay constants remain untouched until their migration PRs.

5. CPU Boost options and Power Mode options are centrally ordered/labeled.

6. Known TDP 1 W / 2 W linked-gap policy is emitted from the shared projection.

7. One explicit generic Device mutation adapter maps validated intents onto the
   existing eight typed IAddonFrontendControl operations.

8. Malformed intents invoke zero feature mutations.

9. TDP four-slider edits require a complete whole-configuration group and call
   SetDeviceTdpAsync exactly once.

10. Every valid mutation attempt returns a fresh authoritative Device page.

11. Profile identity vocabulary is reserved only for the current visible
    Profile parity set; real Profile projection/mutation is still absent.

12. Main UI, qam.js, Overlay UI, Frontend wire, and Overlay wire behavior remain
    unchanged.

13. Frontend and Overlay protocol versions are unchanged from the implementation
    baseline.

14. No new hardware reader, persistence authority, state cache, manager/service,
    registry, generic RPC, or controller-lifecycle state is added.

15. Full test suite and build verification pass.
```

---

## 44. Completion / handoff to SF-V2-04

After SF-V2-03, the code should have this in-process shape:

```text
FrontendDeviceQuickSettingsSnapshot
        ↓
QuickSettingsPageSnapshot(Device)
        ↓
closed shared product semantics

QuickSettingsMutationIntent
        ↓
explicit adapter
        ↓
existing typed Device mutations
        ↓
fresh QuickSettingsPageSnapshot(Device)
```

But neither external surface consumes it yet.

The next PR is:

```text
SF-V2-04 — Frontend/QAM generic Quick Settings RPC seam
```

That PR will make this contract reachable by QamHost through `.Qam` / the existing frontend transport, with the required frontend protocol version bump, while still leaving the visible QAM renderer unchanged until SF-V2-05.

Do not implement SF-V2-04 transport work inside this PR.
