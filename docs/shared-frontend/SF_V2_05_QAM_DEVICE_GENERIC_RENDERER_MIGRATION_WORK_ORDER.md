# Work Order — SF-V2-05: QAM Device Generic Renderer Migration

> **Date:** 2026-09-10  
> **Status:** Ready for implementation  
> **Track:** Shared Frontend V2 / Phase D  
> **Reviewed repository head:** `main` at `c8f4fb4c9423cb9c955acef7fdee42332be9d0ed` after merged PR #505  
> **Previous phase:** SF-V2-04 Frontend/QAM generic Quick Settings RPC seam — merged PR #505  
> **Architecture authority:** `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md`  
> **PR roadmap:** `docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md`  
> **Next phase:** SF-V2-06 `.Overlay` generic Quick Settings v7 transport replacement

---

## 1. Goal

Make Steam QAM the **first real renderer of the shared Quick Settings product model**.

Refactor only the existing **Device** path in:

```text
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
```

so Device UI is rendered from:

```text
QuickSettingsPageSnapshot(Device)
```

and Device mutations are submitted through:

```text
mutateQuickSetting
→ QuickSettingsMutationIntent
→ QuickSettingsMutationResult
```

instead of QAM owning a second Device product definition.

The target ownership becomes:

```text
Runtime typed Device feature truth
        ↓
QuickSettingsPresentation.BuildDevice(...)
        ↓
QuickSettingsPageSnapshot(Device)
        ↓
Frontend/QAM protocol v28
        ↓
QamFrontendBridge
        ↓
qam.js generic Device renderer
        ↓
Steam native QAM controls
```

The fundamental rule is:

> **QAM owns rendering, local preview mechanics, surface admission, and surface lifetime. Runtime/shared Quick Settings owns Device product rows, labels, order, options, ranges, commit policy, grouping, and linked-constraint policy.**

This PR is a product-definition ownership migration, **not a QAM redesign**.

---

## 2. Latest baseline verification

This work order was prepared against the actual post-SF-V2-04 `main`:

```text
c8f4fb4c9423cb9c955acef7fdee42332be9d0ed
```

PR #505 is merged.

Current protocol versions are now:

```text
FrontendTransportProtocol.CurrentVersion = 28
OverlayTransportProtocol.CurrentVersion  = 6
```

SF-V2-04 already provides the complete generic QAM transport seam:

```text
FrontendRpcMethod.CaptureQuickSettingsPage
FrontendRpcMethod.MutateQuickSetting

NamedPipeAddonFrontendClient.CaptureQuickSettingsPageAsync(...)
NamedPipeAddonFrontendClient.MutateQuickSettingAsync(...)

QamFrontendBridge JS allowlist:
  captureQuickSettingsPage
  mutateQuickSetting
```

It also preserves the current Device mutation admission:

```text
Steam.Active == true
AND Steam.AppId == 0
AND Steam.Source == BigPicture
```

and uses the strict generic bridge decoder:

```csharp
QuickSettingsBridgeJson
{
    RespectRequiredConstructorParameters = true
}
```

while keeping legacy `BridgeJson` behavior unchanged.

### 2.1 Current `qam.js` is still the old Device product authority

Current Device refresh still does:

```javascript
const nextDevice = activeGame ? null : await request("captureDeviceQuickSettings");
const nextCpu = nextDevice?.cpuBoost ?? null;
const nextPowerMode = nextDevice?.powerMode ?? null;
const nextTdp = nextDevice?.tdp ?? null;
```

Current Device mutations still call feature-specific bridge methods:

```text
setDeviceCpuBoostEnabled
setDeviceCpuBoostAc
setDeviceCpuBoostDc
setDeviceTdpEnabled
setDeviceTdp
setDevicePowerModeEnabled
setDevicePowerModeAc
setDevicePowerModeDc
```

Current QAM Device logic still owns duplicated product facts, including:

```text
QAM_SLIDER_COMMIT_DELAY_MS = 2000
CPU Boost option labels/order
Power Mode option labels/order
TDP numeric range handling
TDP known-limit gap detection
Device section/row construction
feature-specific pending keys:
  device-cpu-*
  device-tdp
  device-power-*
feature-specific mutation method names
```

That duplication is exactly what SF-V2-05 removes for Device.

### 2.2 Important current code shape

Do not assume Device has an isolated React component.

Current `CpuBoostPanel()` actually contains both:

```text
Device page path when AppId == 0
Profile page path when an active game exists
```

It also shares some local helpers/state between those paths.

Therefore the migration must be **surgical**:

```text
Device branch → generic shared Quick Settings renderer
Profile branch → legacy behavior unchanged
```

Do not rewrite the whole component merely to obtain prettier architecture.

---

## 3. Required reading before implementation

### 3.1 Full1902 authority — mandatory

Read in the current precedence defined by:

```text
docs/Full 1902 Implementation/README.md
```

Then read:

```text
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
```

Frozen invariant:

```text
QAM = disposable frontend/presentation client
Runtime = controller/feature/hardware/persistence authority
```

SF-V2-05 must not participate in:

```text
PID1901/PID1902 ownership
DirectInput ownership
HidHide ownership
VIIPER ownership/teardown
Xbox360/SteamDeck presentation ownership
sleep/hibernate/resume recovery
PnP re-enumeration recovery
restart/shutdown authority
Center M authority transition
OQ4 controller capture
```

### 3.2 Shared Frontend authority

Read:

```text
docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
docs/shared-frontend/SF_V2_01_DEVICE_QUICK_SETTINGS_SHARED_AGGREGATE_WORK_ORDER.md
docs/shared-frontend/SF_V2_02_OVERLAY_DEVICE_QUICK_SETTINGS_TRANSPORT_WORK_ORDER.md
docs/shared-frontend/SF_V2_03_SHARED_QUICK_SETTINGS_PRODUCT_CONTRACT_WORK_ORDER.md
docs/shared-frontend/SF_V2_04_FRONTEND_QAM_GENERIC_QUICK_SETTINGS_RPC_SEAM_WORK_ORDER.md
```

Also read the relevant lifecycle work order:

```text
docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md
```

SF-V2-05 does not modify Overlay/OQ4, but its frontend changes must not create a new cross-surface authority or visibility manager.

### 3.3 Current source — inspect before editing

At minimum inspect the latest versions of:

```text
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw.QamHost/Program.cs

src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
```

Tests:

```text
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsPresentationTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsMutationAdapterTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsInProcessSeamTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
```

---

## 4. Scope

### Production files expected to change

Primary:

```text
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
```

Tests:

```text
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
```

A very small assertion-only update to:

```text
tests/SteamInputAddonforClaw.Tests/QuickSettingsPresentationTests.cs
```

is acceptable if useful to freeze the existing TDP toggle/group distinction.

### Production files that should normally have zero diff

```text
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

src/SteamInputAddonforClaw.Overlay/**
src/SteamInputAddonforClaw.UI/**
```

If the implementation appears to require a new contract field or another protocol bump, stop and re-check the design before proceeding.

---

## 5. Explicit non-goals

Do **not** include any of the following in SF-V2-05:

```text
QAM tab order change
Addon-first/default-tab selection
owner.props.tabs push → unshift experiment
Steam selected-tab manipulation
webpack/component discovery rewrite
new Steam QAM signature probing
custom QAM CSS redesign
new QAM navigation architecture
Profile generic renderer/mutation migration
Profile shared page projection
Intel FPS Limit exposure
Resolution exposure
Overlay v7 work
Overlay Device binding
Main UI migration
new named pipe
new frontend protocol version
Quick Settings schema expansion
new state manager/service/store
new lifecycle state machine
controller/Full1902 changes
```

The separately discussed **Addon tab first/default when QAM opens** work must wait until this migration is complete and stable.

Do not mix it into SF-V2-05.

---

## 6. Frozen architecture for this PR

The QAM architecture remains:

```text
Steam QAM
│
├─ existing Steam native tab shell / injection
│
└─ Addon tab
     │
     ├─ Steam native PanelSection
     ├─ Steam native PanelSectionRow
     ├─ Steam native ToggleField
     ├─ Steam native SliderField
     └─ Steam native typography/classes
```

Ownership:

```text
Steam native components/classes
→ visual primitives

QuickSettingsPageSnapshot
→ Device product definition

qam.js
→ generic mapping from shared row → Steam native control
→ local pending preview mechanics
→ surface admission/lifetime only
```

Do not replace Steam native controls with custom HTML controls.

Do not rewrite current component discovery merely because the Device renderer is changing.

---

## 7. Shared Device page is now the only Device product-definition input

Replace Device refresh from:

```javascript
request("captureDeviceQuickSettings")
```

to:

```javascript
request("captureQuickSettingsPage", {
  pageId: QUICK_SETTINGS_PAGE_DEVICE,
  appId: null,
})
```

The current bridge JSON intentionally serializes enums numerically.

For the current C# enum:

```text
QuickSettingsPageId.Device = 0
```

A small JS ABI constant is acceptable:

```javascript
const QUICK_SETTINGS_PAGE_DEVICE = 0;
```

This is **transport/ABI vocabulary**, not duplicated product presentation policy.

Do not define JS copies of Device row labels/order/options/ranges.

### Refresh behavior

Preserve the current page selection model:

```text
active Steam game AppId > 0
→ legacy Profile path
→ do NOT capture/render Device page

no active game
→ captureQuickSettingsPage(Device)
→ generic Device renderer
```

`captureStatus` remains because QAM surface admission is surface-owned.

`captureActiveGameProfile` remains because Profile is not migrated in this PR.

---

## 8. Bridge JSON / enum boundary — do not regress SF-V2-04

Current bridge behavior is intentionally split:

```text
QamFrontendBridge.BridgeJson
→ camelCase
→ existing legacy enum representation remains numeric

QuickSettingsBridgeJson
→ clone of BridgeJson
→ RespectRequiredConstructorParameters = true
→ used to decode the two generic Quick Settings requests
```

Do not globally add a string-enum converter.

Do not remove `QuickSettingsBridgeJson`.

The review fix from PR #505 is required:

```text
missing PageId / RowId / Kind
→ bounded bridge error
→ zero Runtime mutation
```

### Allowed JS enum knowledge

The generic renderer may define the minimum ABI constants required to interpret structural contract enums, for example:

```javascript
const QS_CONTROL_TOGGLE = 0;
const QS_CONTROL_SLIDER = 1;
const QS_SLIDER_NUMERIC = 0;
const QS_SLIDER_DISCRETE = 1;
const QS_COMMIT_IMMEDIATE = 0;
const QS_COMMIT_TRAILING = 1;
```

Do not create JS constants for every Device RowId and then rebuild a feature switch around them.

The renderer should treat `rowId`, `sectionId`, and `commitGroupId` primarily as **opaque stable identities** used for keys, pending state, and mutation intents.

---

## 9. Device state model in `qam.js`

For the Device path, replace the feature-shaped frontend state:

```text
cpu
tdp
powerMode
Device-specific previews/drafts
```

with one authoritative shared page:

```javascript
const [devicePage, setDevicePage] = React.useState(null);
```

The shared page is authoritative Runtime product state.

Pending slider edits remain surface-local and temporary.

Do not patch pending values into the authoritative page object in place.

Preferred mental model:

```text
devicePage
= latest authoritative page from Runtime

state.qamSliderCommits
= local not-yet-settled Device/Profile drafts

rendered value
= matching pending Device value if present
  else authoritative row.Value
```

The existing global pending map may be reused.

Do not add another Device store/cache/manager.

---

## 10. Generic Device page renderer

Create the smallest clear generic rendering helpers inside `qam.js`.

Conceptually:

```javascript
renderDeviceQuickSettingsPage(page)
renderDeviceQuickSettingsSection(section)
renderDeviceQuickSettingsRow(section, row)
```

Exact names may differ.

### Required iteration rule

Render in the exact order supplied by the page:

```text
page.sections[] order
→ section.rows[] order
```

Do not sort by RowId.

Do not reconstruct TDP/CPU/Power ordering in JS.

Use stable keys derived from shared identities, for example:

```javascript
`qs-section-${section.sectionId}`
`qs-row-${row.rowId}`
```

### Native Steam mapping

Required mapping:

```text
QuickSettingsControlKind.Toggle
→ native.ToggleField

QuickSettingsControlKind.Slider
→ native.SliderField

QuickSettingsSection
→ native.PanelSection

QuickSettingsRow
→ native.PanelSectionRow
```

Continue using current native field label classes / `labelRow(...)` style.

Do not add custom React controls where Steam already supplies the native primitive.

---

## 11. Toggle rendering

A Toggle row must use shared data:

```text
row.label
row.available
row.writable
row.value.booleanValue
row.commitPolicy
```

Effective disabled state is renderer-local admission combined with product writability:

```text
!surfaceDeviceMutationAdmitted
OR !row.available
OR !row.writable
OR immediateMutationBusy
```

Illustrative shape:

```javascript
React.createElement(native.ToggleField, {
  label: row.label,
  checked: row.value?.booleanValue === true,
  disabled: !canMutateDeviceRow(row),
  onChange: value => void commitDeviceImmediate(
    section,
    row,
    makeBooleanValue(row, !!value)),
});
```

Do not identify behavior from label text.

Do not call `setDeviceCpuBoostEnabled`, `setDeviceTdpEnabled`, or `setDevicePowerModeEnabled` directly.

---

## 12. Slider rendering — numeric

For:

```text
SliderSpec.Kind = Numeric
```

use the shared spec directly:

```text
Minimum
Maximum
Step
Suffix
```

Conceptual mapping:

```javascript
React.createElement(native.SliderField, {
  label: labelRow(
    row.label,
    `${effectiveValue}${row.sliderSpec?.suffix ?? ""}`),
  min: row.sliderSpec.minimum,
  max: row.sliderSpec.maximum,
  step: row.sliderSpec.step,
  value: effectiveValue,
  showValue: true,
  disabled: !canMutateDeviceRow(row),
  onChange: next => updateDeviceSliderDraft(section, row, Number(next)),
});
```

Do not preserve the current renderer artifact where PL1 visually uses PL2 max.

The shared projection already exposes the actual semantic PL1/PL2 limits.

Do not re-cap PL1 with Device-specific JavaScript.

---

## 13. Slider rendering — discrete

For:

```text
SliderSpec.Kind = Discrete
```

the **ordered `Options[]` supplied by the page** are authoritative.

Do not use the current Device copies of:

```text
CPU Boost `modes` array
Power Mode labels/names arrays
```

for Device rendering.

### Do not assume option values are contiguous

Even though current CPU Boost values are 0..6 and current Power Mode values are 0..2, the generic renderer should honor the ordered option list rather than treating the product value itself as the slider index.

Preferred mapping:

```text
Runtime/product value
→ find option whose option.value matches
→ SliderField position = option index

SliderField new position
→ option at that index
→ mutation value = option.value
```

Illustrative shape:

```javascript
const options = row.sliderSpec?.options ?? [];
const optionIndex = options.findIndex(
  option => Number(option.value) === Number(effectiveIntegerValue));

if (optionIndex < 0) {
  // Do not guess a fallback product value.
  return null; // or the existing safely-disabled row presentation
}

React.createElement(native.SliderField, {
  label: labelRow(row.label, options[optionIndex].label),
  min: 0,
  max: Math.max(0, options.length - 1),
  step: 1,
  value: optionIndex,
  notchCount: options.length,
  notchTicksVisible: true,
  disabled: !canMutateDeviceRow(row),
  onChange: nextIndex => {
    const option = options[Math.round(Number(nextIndex))];
    if (option) updateDeviceSliderDraft(section, row, option.value);
  },
});
```

No label reconstruction from enum names.

---

## 14. Surface admission remains QAM-owned

Do not move admission into the shared page.

Keep the current QAM Device rule:

```text
status.Steam.Active
AND status.Steam.AppId == 0
AND status.Steam.Source == BigPicture
```

The JS renderer should use it to disable Device editing when the surface is not admitted.

`QamFrontendBridge` remains the actual mutation enforcement boundary.

This is intentional defense-in-depth at two different layers:

```text
qam.js
→ presentation enable/disable

QamFrontendBridge
→ authoritative surface admission
```

Do not duplicate row/value/TDP product validation in the bridge.

---

## 15. Generic Device mutation value construction

Do not send feature-specific payloads.

The generic request is:

```javascript
request("mutateQuickSetting", intent)
```

where `intent` has bridge-camelCase shape equivalent to:

```text
QuickSettingsMutationIntent
├─ pageId
├─ appId
├─ editedRowId
└─ values[]
    ├─ rowId
    └─ value
```

For Device:

```text
pageId = devicePage.pageId
appId = null
editedRowId = row.rowId
```

### Preserve typed value shape

Do not infer a value kind from a Device RowId table.

Use the row/shared value shape.

Conceptually:

```javascript
function makeBooleanValue(row, value) {
  return {
    kind: row.value.kind,
    booleanValue: !!value,
    integerValue: null,
  };
}

function makeIntegerValue(row, value) {
  return {
    kind: row.value.kind,
    booleanValue: null,
    integerValue: Number(value),
  };
}
```

If the row does not carry the expected safe value shape, fail closed locally rather than fabricating `kind = 0/1` from feature knowledge.

The C# `QuickSettingsMutationAdapter` remains final product validation.

---

## 16. Immediate mutation path

Current Device Toggles use:

```text
CommitPolicy = Immediate
```

When an immediate row changes:

```text
cancel pending delayed Device edits in the same section
→ submit one generic mutation immediately
→ receive QuickSettingsMutationResult
→ remove/retire affected pending state
→ install result.Page as authoritative Device page
→ show FailureMessage if Succeeded == false
```

Why same-section cancellation matters:

```text
CPU Boost toggle OFF
→ pending CPU Boost AC/DC slider must not fire later

TDP toggle OFF
→ pending TDP group commit must not fire later

Power Mode toggle OFF
→ pending Power Mode AC/DC slider must not fire later
```

Implement this generically from the section identity stored with pending entries.

Do not hard-code `device-cpu-*`, `device-tdp`, or `device-power-*` cancellation predicates.

A small helper such as:

```javascript
cancelDeviceQuickSettingsCommits(
  pending => pending.sectionId === section.sectionId)
```

is sufficient.

Do not add a mutation queue/service.

---

## 17. Pending key rule — generic Device only

The current Device pending keys are product-specific strings.

Replace them with identity-derived keys:

```text
CommitGroupId == null
→ independent row
→ pending key derived from RowId

CommitGroupId != null
→ shared group
→ pending key derived from CommitGroupId
```

Example:

```javascript
function devicePendingKey(row) {
  return row.commitGroupId == null
    ? `device-row:${row.rowId}`
    : `device-group:${row.commitGroupId}`;
}
```

This is frontend bookkeeping only.

Do not create a Device RowId → feature-name mapping.

### Required behavior

```text
CPU Boost AC
→ independent key

CPU Boost DC
→ independent key

Power Mode AC
→ independent key

Power Mode DC
→ independent key

TDP AC PL1 / AC PL2 / DC PL1 / DC PL2
→ same DeviceTdpConfiguration group key
```

The renderer learns this only from `commitGroupId`.

---

## 18. Critical TDP whole-draft rule — do not change the shared contract

This is the most important implementation detail discovered while reviewing the latest code and SF-V2-03 authority together.

### Existing contract is intentional

Current projection:

```text
DeviceTdpEnabled
→ CommitPolicy = Immediate
→ CommitGroupId = null

four TDP numeric sliders
→ CommitPolicy = TrailingDebounce(2000)
→ CommitGroupId = DeviceTdpConfiguration
```

SF-V2-03 explicitly states:

> The TDP Enable toggle remains Immediate and is not itself a delayed slider group member.

Do **not** “fix” SF-V2-05 by assigning `DeviceTdpConfiguration` to the Enable toggle.

Do not change `QuickSettingsContracts.cs` or `QuickSettingsPresentation.cs` for this.

### But the mutation adapter requires the complete configuration

`QuickSettingsMutationAdapter.TryGetTdpGroup(...)` intentionally requires exactly:

```text
DeviceTdpEnabled = true
DeviceTdpAcPl1
DeviceTdpAcPl2
DeviceTdpDcPl1
DeviceTdpDcPl2
```

for a grouped slider mutation.

Therefore a generic renderer must distinguish:

```text
pending/scheduling membership
→ only rows with the shared CommitGroupId

whole mutation draft payload
→ complete current TDP configuration represented by the containing section
```

### Minimal generic solution for the current closed model

When the first grouped slider edit begins:

1. identify its containing `section`;
2. seed a whole-draft value set from the section's current **value-bearing rows in section order**;
3. require every value needed by that current section to be non-null/safely representable;
4. overlay the edited slider value;
5. apply linked constraints, possibly updating its paired slider value;
6. store the resulting whole draft under the `commitGroupId`-derived pending key.

Further edits in the same group reuse the pending whole draft rather than rebuilding it from a newer authoritative refresh.

Conceptually:

```javascript
function seedWholeSectionDraft(section) {
  const values = [];
  for (const row of section.rows ?? []) {
    if (row.value == null) return null;
    values.push({ rowId: row.rowId, value: cloneQuickSettingsValue(row.value) });
  }
  return values;
}
```

For the current Device TDP section this naturally produces the required five values without any JS knowledge of:

```text
DeviceTdpEnabled
DeviceTdpAcPl1
DeviceTdpAcPl2
DeviceTdpDcPl1
DeviceTdpDcPl2
```

This is sufficient for the **current closed Device model** because TDP is the only grouped Device section and that section represents one whole configuration.

Do not build a generic dependency DSL or add schema fields for hypothetical future grouped sections.

If a future shared page needs a group whose mutation members cannot be represented by this closed current rule, extend the contract in that future focused milestone.

---

## 19. Generic trailing debounce

Device timing must come from:

```text
row.commitPolicy.mode
row.commitPolicy.delayMilliseconds
```

not from a JS Device constant.

Current expected page data is:

```text
Toggle  → Immediate / 0 ms
Slider  → TrailingDebounce / 2000 ms
```

The timer mechanics remain surface-local.

A small token/generation is already used by the current QAM scheduler and is appropriate.

Required observable behavior:

```text
edit slider
→ preview immediately
→ schedule using row delay

edit again before timer fires
→ clear old timer
→ latest draft wins
→ restart delay

old in-flight completion after newer draft exists
→ token mismatch
→ do not replace newer draft/page
```

Do not add epochs/barriers/revision vectors.

### Removing the old Device delay constant

Remove:

```javascript
const QAM_SLIDER_COMMIT_DELAY_MS = 2000;
```

from Device ownership.

Because Profile is intentionally not migrated in SF-V2-05, it is acceptable to retain the legacy Profile timing as a clearly Profile-scoped temporary constant, for example:

```javascript
const PROFILE_SLIDER_COMMIT_DELAY_MS = 2000;
```

or an equivalent existing Profile-only mechanism.

The important rule is:

> **No Device generic slider schedule may read a hard-coded 2000 ms value.**

Profile product duplication is removed in its later migration PR, not here.

---

## 20. Reuse scheduler mechanics without rewriting Profile

Current `scheduleQamSliderCommit(...)` already provides useful mechanics:

```text
one pending map
clear previous timer
monotonic token
pre-request token check
post-request token check
remove current entry on settle
ignore stale completion
```

Preserve these properties.

Two acceptable implementation directions are:

### Option A — small generic scheduling primitive

Extract only the timer/token mechanics:

```javascript
scheduleDelayedCommit(key, pending, delayMs, execute, onSettled)
```

Then:

```text
Device
→ delayMs from row.commitPolicy
→ execute = request("mutateQuickSetting", intent)

legacy Profile
→ existing feature-specific request
→ existing Profile delay
```

### Option B — minimally extend the current scheduler

Allow it to receive a delay and generic execute callback while leaving Profile behavior equivalent.

Do not perform a broad Profile rewrite to make the helper aesthetically uniform.

The review target is correctness and one clear Device generic path, not maximum helper unification.

---

## 21. Linked slider constraints — metadata-driven Device behavior

Device must stop owning the current hard-coded TDP limit-shape policy:

```text
8/30 + 8/37 → gap 1
8/35 + 8/45 → gap 2
```

The shared page already supplies:

```text
linkedSliderConstraints[]
├─ lowerRowId
├─ upperRowId
└─ minimumGap
```

The Device draft engine must apply these constraints using row identities and the rows' own `SliderSpec` bounds.

### Required local behavior

When the edited row is the lower member:

```text
upper < lower + minimumGap
→ adjust upper upward when valid
→ otherwise clamp lower against upper row's allowed maximum as needed
```

When the edited row is the upper member:

```text
lower > upper - minimumGap
→ adjust lower downward when valid
→ otherwise clamp against lower row's allowed minimum as needed
```

Use the current behavior as the observable reference, but derive:

```text
gap
lower/upper identities
min/max
```

from the page metadata.

Do not match labels containing `PL1`/`PL2` in the Device path.

Do not inspect known Claw limit tuples in the Device path.

### Profile exception for this PR

Current Profile is still legacy and currently reuses the old `adjustTdpPair(...)` logic.

Do not migrate Profile merely to delete the literals globally.

If needed for clarity, rename/scope the old helper to make it explicit that it is now **legacy Profile-only**.

The Device path must not depend on it after SF-V2-05.

---

## 22. Pending draft survives same-page invalidation

Preserve the architecture rule:

```text
Device slider draft pending
→ StateInvalidated arrives
→ capture a fresh authoritative Device page
→ unrelated rows use new Runtime state
→ pending row/group values remain visible
→ pending timer remains current
```

Do not overwrite the pending Device draft simply because `refresh()` received a newer page.

Do not mutate the fresh page object to fake authority.

Instead resolve effective display value at render time:

```text
matching pending Device entry contains this RowId
→ pending value

otherwise
→ row.value from authoritative devicePage
```

For a grouped TDP draft, the pending entry owns the whole current group/section draft until it settles or is explicitly retired.

---

## 23. Context/admission loss retires Device pending work

Pending survival applies while the same Device surface remains valid.

A normal product lifecycle can change from:

```text
QAM Device / no game
→ user launches a Steam game
→ Profile path becomes active
```

A Device delayed mutation is no longer admitted once `AppId != 0`.

Do not leave an old Device timer alive merely so it can fire into the bridge and be rejected.

When refresh detects that Device mutation admission/context has been lost, retire Device generic pending commits with the existing cancellation mechanism.

Examples:

```text
AppId becomes non-zero
→ cancel Device delayed commits

Big Picture admission disappears while QAM document remains alive
→ cancel Device delayed commits
```

This is a normal lifecycle cleanup, not a new race state machine.

It also prevents a 2-second pending Device slider edit from producing a spurious bridge failure immediately after game launch.

Profile's existing AppId-change retirement behavior remains unchanged.

---

## 24. Authoritative mutation result wins

For the current pending token only:

```text
mutateQuickSetting returns
→ remove/retire that pending entry
→ install result.page as authoritative Device page
→ if succeeded == false, show failureMessage
```

This applies on both success and typed feature failure.

Important:

```text
Succeeded == false
DOES NOT mean restore the old local draft.
```

The C# adapter always returns a fresh page because an underlying apply failure may still have persisted a desired value.

Therefore:

```text
result.page
= authority
```

on both success and failure.

Do not issue a second feature-specific readback.

A generic recapture is unnecessary merely to reproduce data already carried by the mutation result, unless current surrounding QAM status refresh genuinely requires one for a separate surface fact.

Do not replace `result.page` with a locally patched page.

---

## 25. Transport failure behavior

Keep current fail-close behavior for a real QAM bridge/transport failure.

Examples:

```text
request("mutateQuickSetting") throws
bridge disconnects
malformed response
```

Expected:

```text
retire affected stale editable frontend state
show bounded QAM error
Runtime survives
controller ownership survives
```

Do not turn a frontend transport failure into:

```text
controller teardown
PID restoration
HidHide mutation
VIIPER teardown
QamHost reconnection state machine
```

No polling/reconnect framework is added.

---

## 26. Device product hard-coding that must be removed

After SF-V2-05, the Device path in `qam.js` must no longer define or depend on copies of:

```text
Device section order
Device row order
Device row labels
CPU Boost Device option labels/order
Power Mode Device option labels/order
TDP Device numeric min/max/step
TDP Device limit-shape → gap policy
QAM Device 2000 ms delay policy
feature-specific Device mutation method names
feature-specific Device pending keys
```

In particular these old Device bridge callsites must disappear from `qam.js`:

```text
captureDeviceQuickSettings
setDeviceCpuBoostEnabled
setDeviceCpuBoostAc
setDeviceCpuBoostDc
setDeviceTdpEnabled
setDeviceTdp
setDevicePowerModeEnabled
setDevicePowerModeAc
setDevicePowerModeDc
```

### Important Profile distinction

Some of the same labels/options currently appear in the legacy Profile implementation.

Do **not** delete working Profile behavior merely to make a global source search for strings return zero results.

Tests must distinguish:

```text
Device generic path
vs
legacy Profile path
```

For example, the old CPU Boost label array may remain temporarily if Profile still consumes it, but Device renderer must consume `row.sliderSpec.options` instead.

---

## 27. Remove old feature-specific Device bridge operations

Once no current `qam.js` Device callsite uses them, remove the temporary SF-V2-04 bridge operations from `QamFrontendBridge`:

```text
captureDeviceQuickSettings
setDeviceCpuBoostEnabled
setDeviceCpuBoostAc
setDeviceCpuBoostDc
setDeviceTdpEnabled
setDeviceTdp
setDevicePowerModeEnabled
setDevicePowerModeAc
setDevicePowerModeDc
```

Keep:

```text
captureQuickSettingsPage
mutateQuickSetting
```

and keep the generic Device admission helper:

```text
EnsureDeviceMutationAdmittedAsync
```

### Remove now-unused bridge helpers only if truly unused

After the old Device path is removed, check whether these remain referenced:

```text
MutateAsync(...)
DecodeTdpConfiguration(...)
```

If they are Device-only, delete them and update obsolete tests.

Do **not** remove:

```text
DecodePowerMode(...)
```

if the legacy Profile bridge still uses it.

### Do not remove focused frontend client methods

This cleanup is **QamFrontendBridge-only**.

Do not remove from `NamedPipeAddonFrontendClient`:

```text
CaptureDeviceQuickSettingsAsync
SetDeviceCpuBoost*
SetDeviceTdp*
SetDevicePowerMode*
```

They are still valid typed frontend APIs used by Main UI/other code.

Do not remove the corresponding frontend RPCs.

---

## 28. Preserve Profile behavior exactly

SF-V2-05 must not migrate Profile.

Current Profile path continues to use:

```text
captureActiveGameProfile
setActiveGameProfileEnabled
setActiveGameCpuBoostEnabled
setActiveGameCpuBoostAc
setActiveGameCpuBoostDc
setActiveGameTdpEnabled
setActiveGameTdp
setActiveGamePowerModeEnabled
setActiveGamePowerModeAc
setActiveGamePowerModeDc
setActiveGameFpsLimit*
```

where currently applicable.

Also preserve:

```javascript
const SHOW_INTEL_FPS_LIMIT = false;
```

Do not expose Intel FPS Limit merely because backend methods exist.

Do not add Resolution to QAM.

If a helper previously served both Device and Profile, separate or rename it only as much as needed so Device no longer depends on duplicated product policy.

No Profile functional redesign.

---

## 29. Preserve current Steam-native discovery/injection

Do not modify the current successful QAM integration machinery, including:

```text
webpackChunksteamui / webpackChunk_steamclient capture
QAM_SIGNATURES
findQamRenderers
findReact
findCommonUiModule
findToggleField
findSliderField
findPanelComponents
findNativeClassStyles
findReactNode bounded walker
REACT_WALK_NODE_BUDGET
resolveComponentTarget
patchTabsProducer
fiber/live patch restoration
TAB_MARKER
existing Addon tab key
```

Do not change:

```javascript
owner.props.tabs.push(buildAddonTab(...))
```

in this PR merely to make Addon first.

That is a separate post-SF-V2-05 UI task.

---

## 30. UI parity rule

This migration should preserve current intended Device content:

```text
TDP
CPU Boost
Windows Power Mode
```

with the same row ordering and control types — now supplied by the shared page.

Use Steam native controls and current native classes.

Do not intentionally redesign font size, padding, section spacing, tab styling, or QAM chrome here.

### Avoid hard-coded exceptions just to preserve an old renderer quirk

If exact old chrome conflicts with removing Device product duplication, prefer:

```text
shared section/row metadata
+ Steam native default behavior
```

over adding:

```text
if SectionId == DevicePowerMode then title = ...
```

Do not create another Device-specific layout table.

A small visual difference caused solely by replacing a duplicated hard-coded product rule with the shared product metadata is preferable to restoring a second authority.

Major visual redesign remains out of scope.

---

## 31. Suggested generic Device helper shape

The following is illustrative, not a requirement to copy names exactly.

### 31.1 Flatten / find rows without feature switches

```javascript
function deviceRows(page) {
  const rows = [];
  for (const section of page?.sections ?? []) {
    for (const row of section?.rows ?? []) {
      rows.push({ section, row });
    }
  }
  return rows;
}
```

### 31.2 Effective pending value

```javascript
function pendingDeviceValue(rowId) {
  for (const entry of state.qamSliderCommits?.values?.() ?? []) {
    if (!entry?.deviceQuickSettings) continue;
    const key = String(rowId);
    if (Object.prototype.hasOwnProperty.call(entry.values ?? {}, key)) {
      return entry.values[key];
    }
  }
  return null;
}

function effectiveDeviceValue(row) {
  return pendingDeviceValue(row.rowId) ?? row.value;
}
```

Adapt to the existing map/entry shape rather than adding needless wrappers.

### 31.3 Pending identity

```javascript
function devicePendingKey(row) {
  return row.commitGroupId == null
    ? `device-row:${row.rowId}`
    : `device-group:${row.commitGroupId}`;
}
```

### 31.4 Generic intent

```javascript
function deviceIntent(page, row, values) {
  return {
    pageId: page.pageId,
    appId: null,
    editedRowId: row.rowId,
    values,
  };
}
```

No Device method-name switch.

---

## 32. Generic grouped-draft sketch

A simple current-scope implementation may look conceptually like:

```javascript
function cloneValue(value) {
  return value == null ? null : { ...value };
}

function seedGroupedDraft(section) {
  const values = {};
  const rowOrder = [];

  for (const row of section.rows ?? []) {
    if (row.value == null) return null;
    const key = String(row.rowId);
    values[key] = cloneValue(row.value);
    rowOrder.push(row.rowId);
  }

  return { values, rowOrder };
}
```

On edit:

```javascript
const key = devicePendingKey(row);
const current = state.qamSliderCommits?.get(key);
const draft = current?.deviceQuickSettings
  ? cloneExistingDraft(current)
  : row.commitGroupId == null
      ? seedIndependentDraft(row)
      : seedGroupedDraft(section);

if (!draft) return;

draft.values[String(row.rowId)] = makeIntegerValue(row, nextProductValue);
applyLinkedConstraints(devicePage, draft, row.rowId);
```

At grouped commit:

```javascript
const values = draft.rowOrder.map(rowId => ({
  rowId,
  value: draft.values[String(rowId)],
}));

await request("mutateQuickSetting", {
  pageId: devicePage.pageId,
  appId: null,
  editedRowId: row.rowId,
  values,
});
```

For independent sliders, send exactly one `Values` item.

Do not make the C# adapter more permissive just to simplify JS.

---

## 33. Linked-constraint sketch

Conceptually:

```javascript
function applyLinkedConstraints(page, draft, editedRowId) {
  for (const constraint of page?.linkedSliderConstraints ?? []) {
    if (constraint.lowerRowId !== editedRowId &&
        constraint.upperRowId !== editedRowId) continue;

    const lower = findRow(page, constraint.lowerRowId);
    const upper = findRow(page, constraint.upperRowId);
    if (!lower || !upper) continue;

    const lowerValue = draft.values[String(lower.rowId)]?.integerValue;
    const upperValue = draft.values[String(upper.rowId)]?.integerValue;
    const gap = Number(constraint.minimumGap);
    if (!Number.isFinite(lowerValue) || !Number.isFinite(upperValue) || gap <= 0) continue;

    // Preserve current lower/upper correction semantics, but use:
    // lower.sliderSpec.minimum / maximum
    // upper.sliderSpec.minimum / maximum
    // constraint.minimumGap
  }
}
```

No label parsing.

No limit-tuple switch.

No generalized constraint graph.

---

## 34. QAM bridge cleanup tests

Update `QamFrontendContractTests` so the SF-V2-04 transitional assertion is replaced.

Current test intentionally says the generic seam exists **alongside** transition Device methods.

After SF-V2-05 the desired bridge contract is:

```text
captureQuickSettingsPage   present
mutateQuickSetting         present
legacy feature-specific Device bridge names absent
EnsureDeviceMutationAdmittedAsync present
```

Conceptual assertions:

```csharp
Assert.Contains("\"captureQuickSettingsPage\"", bridge);
Assert.Contains("\"mutateQuickSetting\"", bridge);

Assert.DoesNotContain("\"captureDeviceQuickSettings\"", bridge);
Assert.DoesNotContain("\"setDeviceCpuBoostEnabled\"", bridge);
Assert.DoesNotContain("\"setDeviceCpuBoostAc\"", bridge);
Assert.DoesNotContain("\"setDeviceCpuBoostDc\"", bridge);
Assert.DoesNotContain("\"setDeviceTdpEnabled\"", bridge);
Assert.DoesNotContain("\"setDeviceTdp\"", bridge);
Assert.DoesNotContain("\"setDevicePowerModeEnabled\"", bridge);
Assert.DoesNotContain("\"setDevicePowerModeAc\"", bridge);
Assert.DoesNotContain("\"setDevicePowerModeDc\"", bridge);
```

Keep the deterministic generic admission behavior tests added in PR #505.

Keep the malformed-required-identity fail-close tests.

If `DecodeTdpConfiguration` is deleted because it becomes unused, replace/remove its now-obsolete direct unit test rather than keeping dead production code only for a test.

---

## 35. QAM JS contract tests — generic Device read/mutation

Update source-contract tests to assert:

```text
request("captureQuickSettingsPage") present
request("mutateQuickSetting") present
request("captureDeviceQuickSettings") absent
all old setDevice* bridge calls absent
```

Do not weaken Profile assertions.

Add a focused Device-renderer slice/test proving the Device path uses:

```text
page.sections
section.rows
row.label
row.controlKind
row.available
row.writable
row.value
row.sliderSpec
row.commitPolicy
row.commitGroupId
page.linkedSliderConstraints
```

The test should not merely assert those words exist somewhere in the file; slice the generic Device helper region where practical.

---

## 36. QAM JS contract tests — product data is consumed, not reconstructed

For the Device generic renderer, prove:

### Order / labels

```text
sections/rows are iterated in payload order
row.label is passed to native controls
```

### Numeric slider

```text
minimum/max/step come from row.sliderSpec
```

### Discrete slider

```text
options come from row.sliderSpec.options
option.label is displayed
option.value is submitted
```

### Commit policy

```text
delay comes from row.commitPolicy.delayMilliseconds
```

### Commit grouping

```text
null group → RowId-derived pending key
non-null group → CommitGroupId-derived pending key
```

### Constraints

```text
lowerRowId / upperRowId / minimumGap are consumed from page.linkedSliderConstraints
```

Do not require CPU Boost/Power Mode labels to disappear globally from `qam.js` while legacy Profile still uses them.

Instead prove the **Device renderer region** does not depend on those legacy arrays.

---

## 37. TDP contract regression test

Because it is tempting to make the Enable toggle a group member merely to simplify JS, add a small explicit regression assertion if not already present:

```csharp
Assert.Null(
    FindRow(page, QuickSettingsRowId.DeviceTdpEnabled).CommitGroupId);
```

and keep:

```csharp
foreach (var slider in the four Device TDP numeric rows)
    Assert.Equal(
        QuickSettingsCommitGroupId.DeviceTdpConfiguration,
        FindRow(page, slider).CommitGroupId);
```

This test is documentation of the existing contract, not a production change.

---

## 38. Pending/invalidation source-contract tests

Preserve the current stale-completion guard:

```javascript
if (state.qamSliderCommits.get(key)?.token !== token) return;
```

both before applying a request result and in the failure path.

Add/update focused assertions proving:

```text
same-page refresh does not clear current Device pending entries
rendered Device value checks pending draft before row.value
current result settles/removes only its current pending key
a stale old completion cannot install its page over a newer draft
```

Do not add a second token system specifically for generic Device rows.

Reuse the current scheduler generation/token.

---

## 39. Admission/context retirement tests

Add a source-contract or focused behavior test proving Device pending commits are retired when the Device surface loses validity.

Important realistic sequence:

```text
no game / Device slider pending
→ game AppId appears
→ Device pending commit is cancelled
→ legacy Profile view takes over
→ no delayed Device bridge error
```

Do not add an epoch/state machine.

Use the existing pending cancellation mechanism.

---

## 40. Mutation result tests

Prove the generic Device path consumes:

```text
result.page
result.succeeded
result.failureMessage
```

Required settlement:

```text
success
→ current pending clears
→ result.page becomes authoritative

Succeeded=false
→ current pending clears
→ result.page STILL becomes authoritative
→ failureMessage shown
```

Do not test/implement optimistic rollback to the old page.

---

## 41. Native QAM regression tests remain green

Do not weaken existing coverage for:

```text
webpack runtime capture
native ToggleField discovery
native SliderField discovery
PanelSection / PanelSectionRow discovery
native class discovery
bounded React walker
component shape resolution
nested producer patch/restore
live Fiber patch/restore
install/uninstall idempotency
old-document response retirement
CDP send serialization
QamHost teardown gate
```

The Device renderer migration is not permission to simplify away proven QAM lifecycle safety.

---

## 42. No protocol changes

Expected after SF-V2-05:

```text
FrontendTransportProtocol.CurrentVersion == 28
OverlayTransportProtocol.CurrentVersion  == 6
```

No frontend protocol bump.

No Overlay protocol bump.

Do not add another Quick Settings RPC.

The generic seam required by this renderer already landed in SF-V2-04.

---

## 43. No `.Overlay` changes

SF-V2-05 must leave current Overlay v6 untouched.

Still expected:

```text
DeviceQuickSettingsState
DeviceMutationRequest
DeviceMutationResult
OverlayDeviceMutationKind
OverlayDeviceMutationDispatch
```

SF-V2-06 owns replacement with generic Quick Settings v7.

Do not opportunistically share the new QAM JS algorithm with Overlay C#.

The two renderers share **product metadata**, not renderer implementation code.

---

## 44. No Main UI changes

Main UI continues using its current desktop Device APIs and UI.

Do not move Main UI onto the schema renderer.

Do not delete focused typed Device APIs merely because QAM no longer calls them directly.

---

## 45. Failure-policy matrix

Preserve these boundaries:

```text
Malformed JS generic intent
→ QuickSettingsBridgeJson / bridge rejects
→ zero Runtime mutation

QAM Device not admitted
→ QamFrontendBridge rejects
→ zero Runtime mutation

Product-invalid typed intent
→ QuickSettingsMutationAdapter returns Succeeded=false
→ zero wrong typed mutation
→ fresh authoritative Device page

Valid mutation + feature apply/persistence failure
→ generic result Succeeded=false
→ FailureMessage preserved
→ fresh result.Page wins

Same-page StateInvalidated while slider pending
→ fresh base page captured
→ pending Device draft remains visible

Device context/admission lost
→ pending Device delayed work retired

QAM transport failure
→ QAM fails closed locally
→ Runtime/controller survives
```

Do not collapse these into one generic lifecycle failure.

---

## 46. Overengineering guardrails

Do not add:

```text
QuickSettingsRenderer class hierarchy
QAM ViewModel framework
feature registry
page registry
row registry
plugin schema
JSON schema validator
reflection dispatcher
Device feature switch service
pending-draft manager class
mutation queue service
cross-surface state store
revision vector
epoch/barrier protocol
new lock hierarchy
retry framework
QAM reconnect state machine
polling loop
```

The current product needs one simple JS renderer for two control kinds and one pending-map mechanism.

Prefer small functions and current state ownership.

---

## 47. Expected diff shape

A healthy SF-V2-05 diff should look approximately like:

```text
qam.js
  - old Device aggregate decomposition
  - old Device-specific CPU/TDP/Power renderer blocks
  - old Device hard-coded product arrays/policies where no longer needed by Profile
  - old Device feature-specific mutation callsites
  - old Device feature-specific pending keys
  + one Device page state
  + one generic section/row renderer
  + generic numeric/discrete SliderField adapters
  + generic immediate mutation path
  + generic delayed pending key/draft path
  + linked-constraint application from page metadata
  + Device-context pending retirement
  ~ preserve Profile branch behavior

QamFrontendBridge.cs
  - transition captureDeviceQuickSettings allowlist
  - eight old feature-specific Device mutations
  - now-unused Device-only bridge helper(s)
  + keep generic capture/mutation + admission
  + keep strict generic decoder

QamFrontendContractTests.cs
  ~ transition assertions replaced with generic Device assertions
  + metadata-driven renderer/source-contract coverage
  + old Device method absence
  + pending/admission/result behavior guards

QamFrontendBridgeTests.cs
  ~ keep generic admission/fail-close coverage
  - obsolete Device-only decode test if helper is removed

QuickSettingsPresentationTests.cs
  + optional explicit TDP toggle CommitGroupId == null assertion
```

Unexpected changes to Contracts/FrontendTransport/Overlay/Full1902 should trigger reassessment.

---

## 48. Concrete implementation sequence

Recommended order:

### Step 1 — establish generic Device page state/read

Replace Device aggregate capture with:

```text
captureQuickSettingsPage(Device)
```

while keeping the old Device renderer temporarily if useful during coding.

Verify page JSON shape in current bridge semantics.

### Step 2 — add generic row value/render helpers

Implement:

```text
effective Device value
Toggle mapping
Numeric Slider mapping
Discrete Slider mapping
section/row iteration
```

Keep Steam discovery untouched.

### Step 3 — generic immediate mutation

Move Device toggles to:

```text
mutateQuickSetting
```

with same-section pending retirement.

### Step 4 — generic independent delayed sliders

Move CPU Boost/Power Mode Device sliders to RowId-driven pending keys and row policy delay.

### Step 5 — grouped TDP draft

Use CommitGroupId for one shared pending key.

Seed whole mutation values from the current TDP section.

Apply shared linked constraints.

Do not alter the toggle group metadata.

### Step 6 — authoritative settlement/invalidation

Ensure pending overlay survives same-page refresh and `result.page` wins on current settlement.

### Step 7 — retire Device pending on context loss

Cover no-game → game transition.

### Step 8 — delete old Device product implementation

Remove now-unused Device arrays/functions/method names/keys while keeping Profile copies still needed.

### Step 9 — remove old QAM bridge Device allowlist

Only after qam.js has zero callsites.

### Step 10 — tests/full verification

Update contract tests and run full suite.

---

## 49. Validation commands

Run focused tests during implementation, then complete at minimum:

```powershell
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Focused coverage should include at least:

```text
QamFrontendContractTests
QamFrontendBridgeTests
QuickSettingsPresentationTests
QuickSettingsMutationAdapterTests
QuickSettingsInProcessSeamTests
FrontendNamedPipeTransportTests
OverlayDeviceQuickSettingsTransportTests
```

No new warnings.

Do not declare completion from focused tests alone.

---

## 50. Manual QAM verification checklist

Because this PR changes the real Steam CEF renderer path, perform a focused real-QAM check when hardware/runtime access is available.

### Device page

```text
[ ] Open Steam Big Picture QAM with no game running
[ ] Addon Device page renders
[ ] TDP / CPU Boost / Windows Power Mode content/order is correct
[ ] controls are Steam native ToggleField / SliderField
[ ] no custom HTML control regression
```

### CPU Boost

```text
[ ] Toggle immediate mutation works
[ ] AC/DC sliders preview immediately
[ ] 2-second current shared delay behavior is preserved
[ ] rapid repeat movement commits latest value
[ ] seven option labels/order are correct
```

### TDP

```text
[ ] Toggle immediate mutation works
[ ] all four sliders render from shared ranges
[ ] PL1 uses true shared PL1 maximum
[ ] rapid edits across multiple TDP sliders share one pending draft
[ ] AC linked PL1/PL2 constraint applies locally
[ ] DC linked PL1/PL2 constraint applies locally
[ ] one grouped commit submits latest whole configuration
```

### Power Mode

```text
[ ] Toggle works
[ ] AC/DC discrete sliders use shared option labels/order
[ ] delayed commit/latest-wins behavior works
```

### Invalidation / context

```text
[ ] StateInvalidated during a pending Device slider does not visibly erase the pending draft
[ ] current mutation settlement replaces draft with Runtime page
[ ] typed mutation failure shows error and still adopts authoritative result.page
[ ] start a Steam game while Device slider is pending → pending Device edit is retired cleanly
[ ] Profile page still behaves as before
[ ] exit game → Device page returns correctly
```

### Lifetime

```text
[ ] close/reopen QAM without duplicate tab/pending state leak
[ ] QamHost teardown remains normal
[ ] no polling introduced
[ ] controller presentation/Full1902 state unaffected
```

---

## 51. Source cleanup checklist

Before opening the PR, search `qam.js` and verify:

```text
[ ] request("captureDeviceQuickSettings") is gone
[ ] request("setDeviceCpuBoostEnabled") is gone
[ ] request("setDeviceCpuBoostAc") is gone
[ ] request("setDeviceCpuBoostDc") is gone
[ ] request("setDeviceTdpEnabled") is gone
[ ] request("setDeviceTdp") is gone
[ ] request("setDevicePowerModeEnabled") is gone
[ ] request("setDevicePowerModeAc") is gone
[ ] request("setDevicePowerModeDc") is gone
[ ] QAM_SLIDER_COMMIT_DELAY_MS is gone
[ ] Device generic renderer uses row.label
[ ] Device numeric sliders use sliderSpec min/max/step/suffix
[ ] Device discrete sliders use sliderSpec.options
[ ] Device delay uses commitPolicy.delayMilliseconds
[ ] Device pending identity uses rowId/commitGroupId
[ ] Device linked correction uses linkedSliderConstraints
[ ] Device TDP path does not detect known limit tuples
[ ] Device TDP path does not parse PL1/PL2 labels
[ ] TDP Enable remains Immediate and group-null in shared projection
[ ] legacy Profile methods still exist
[ ] SHOW_INTEL_FPS_LIMIT remains false
```

Search `QamFrontendBridge.cs`:

```text
[ ] captureQuickSettingsPage remains
[ ] mutateQuickSetting remains
[ ] EnsureDeviceMutationAdmittedAsync remains
[ ] QuickSettingsBridgeJson remains strict
[ ] all old feature-specific Device QAM allowlist entries are gone
[ ] unused Device-only bridge helpers are gone
[ ] Profile bridge operations remain
```

---

## 52. Acceptance criteria

SF-V2-05 is complete only when all of the following are true:

```text
1. QAM Device reads QuickSettingsPageSnapshot(Device) through captureQuickSettingsPage.

2. QAM Device no longer reads captureDeviceQuickSettings directly.

3. Device sections and rows render in shared payload order.

4. Device labels come from shared row/section metadata rather than a Device JS product table.

5. Toggle rows use Steam native ToggleField.

6. Slider rows use Steam native SliderField.

7. Numeric sliders use shared min/max/step/suffix.

8. Discrete sliders use shared ordered option values/labels without assuming contiguous product values.

9. Device surface admission remains Big Picture active + AppId == 0 and stays enforced by QamFrontendBridge.

10. Device mutations use only mutateQuickSetting / QuickSettingsMutationIntent.

11. Immediate rows commit immediately and retire pending same-section Device slider work.

12. Independent Device sliders use RowId-derived pending identity.

13. Grouped TDP sliders use CommitGroupId-derived pending identity and one whole pending draft.

14. TDP Enable remains Immediate with CommitGroupId == null; no shared schema change is made to simplify JS.

15. Grouped TDP intent contains the complete five-value current configuration draft required by QuickSettingsMutationAdapter without hard-coded Device RowId logic in JS.

16. Device linked slider correction is driven by linkedSliderConstraints + row slider bounds, not hard-coded Claw limit tuples or PL1/PL2 label parsing.

17. Device delayed commit timing is driven by row.commitPolicy.delayMilliseconds; QAM_SLIDER_COMMIT_DELAY_MS is removed as Device product policy.

18. Pending Device draft survives same-page StateInvalidated refresh.

19. Newer pending Device draft cannot be replaced by a stale older completion; existing token/generation protection is preserved.

20. The current mutation result page becomes authoritative on both success and Succeeded=false.

21. Device pending delayed work is retired when game/admission context makes Device mutation no longer valid.

22. Old feature-specific Device method names no longer appear as qam.js request callsites.

23. Old feature-specific Device QamFrontendBridge allowlist operations are removed after their JS callsites are gone.

24. Focused NamedPipeAddonFrontendClient typed Device APIs/RPCs remain available for Main UI/other code.

25. Legacy Profile QAM behavior remains unchanged and is not migrated in this PR.

26. Steam webpack/native component discovery and outer QAM tab injection are unchanged.

27. Addon-first/default-tab behavior is NOT implemented in this PR.

28. FrontendTransportProtocol remains 28.

29. OverlayTransportProtocol remains 6 and no Overlay production code changes.

30. No Main UI production behavior changes.

31. No controller/HidHide/VIIPER/PID/Full1902 lifecycle behavior changes.

32. No polling, generic manager/service, registry, schema engine, revision vector, or new lifecycle state machine is added.

33. Debug/Release builds, full Release tests, focused QAM/shared-frontend tests, and git diff --check pass with no new warnings.
```

---

## 53. Handoff to SF-V2-06

After SF-V2-05, the intended state is:

```text
Runtime
  QuickSettingsPageSnapshot(Device)
  QuickSettingsMutationIntent
  QuickSettingsMutationResult
        │
        ├─ .Frontend/.Qam v28
        │    → QAM generic Device renderer LIVE
        │    → old Device QAM bridge methods removed
        │
        └─ .Overlay v6
             → still temporary Device-specific transport
             → no generic Device binding yet
```

That asymmetry is intentional.

The next Shared Frontend PR is:

```text
SF-V2-06
→ replace .Overlay v6 Device-specific product wire
→ generic Quick Settings v7 transport
→ preserve OQ4 non-blocking/lifecycle guarantees
```

Do not pull SF-V2-06 work into this PR.
