# Work Order — QAM-NATIVE-01: Steam-Native Surface + Interaction Recovery

> **Date:** 2026-09-12  
> **Status:** Ready for implementation  
> **Reviewed baseline:** `main` at `0ec512a5bce2204e86f04096fde5464753b08a6a`  
> **Primary source:** current SteamAddonforClaw `main`  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and the documents it references  
> **Shared frontend authority:** `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md`, `SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md`, `SF_V2_08_SHARED_PROFILE_PROJECTION_DISPATCH_QAM_GENERIC_MIGRATION_WORK_ORDER.md`, and `SF_V2_09_OVERLAY_PROFILE_PUBLICATION_GENERIC_BINDING_WORK_ORDER.md`  
> **External implementation reference:** WSGM branch `2.0`, pinned `KillerPixelCrew/steam-ui-toolkit` commit `13ce887fef7828ab56dd065b545b4747e6131880`  
> **Scope:** Restore reliable QAM control interaction and make the existing one-tab surface use Steam-native component discovery/presentation. This PR does **not** implement the later Device/Profile two-tab topology.

---

## 1. Goal

Fix the current Steam QAM surface so that the Addon's existing shared Quick Settings controls are actually usable with controller/keyboard input, while simplifying the QAM-specific policy and making the presentation follow Steam's native Quick Access Menu component model more closely.

This PR must deliver a clean working baseline before the separate two-tab PR.

Target ownership after this PR:

```text
Runtime feature/profile state + mutation authority
        ↓
QuickSettingsPresentation
        ↓
QuickSettingsPageSnapshot
        ↓
QamFrontendBridge
        ↓
qam.js generic renderer
        ↓
Steam native PanelSection / PanelSectionRow / ToggleField / SliderField
```

The existing shared Device/Profile product model remains authoritative.

Do **not** duplicate Device/Profile labels, ranges, options, or mutation policy in JavaScript.

---

## 2. Required documents and source to read first

Read these before editing:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
docs/shared-frontend/SF_V2_08_SHARED_PROFILE_PROJECTION_DISPATCH_QAM_GENERIC_MIGRATION_WORK_ORDER.md
docs/shared-frontend/SF_V2_09_OVERLAY_PROFILE_PUBLICATION_GENERIC_BINDING_WORK_ORDER.md

src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw.QamHost/Program.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw/Profiles/Performance/CpuBoostRuntime.cs
src/SteamInputAddonforClaw/Profiles/Performance/TdpRuntime.cs
src/SteamInputAddonforClaw/Profiles/Performance/PowerModeRuntime.cs

tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsMutationAdapterTests.cs
```

External reference only:

```text
KillerPixelCrew/WSGM
branch: 2.0
external/steam-ui-toolkit -> 13ce887fef7828ab56dd065b545b4747e6131880

KillerPixelCrew/steam-ui-toolkit
src/SteamUiToolkit/SteamUiAssets/Source/components.ts
```

Do not add WSGM or `steam-ui-toolkit` as a runtime/package/submodule dependency.

---

## 3. Current source state verified for this work order

### 3.1 Current QAM injection path is not the primary failure

The reviewed 2026-09-12 QamHost device log showed the important installation chain completing:

```text
webpack captured
QAM renderers found
React found
native controls reported resolved
outer renderer patched
nested producer patched/invoked
tabs owner found
Addon tab inserted
QAM injection succeeded
```

The Addon tab also remained installed for the live session until normal teardown.

Therefore this PR must not start by redesigning CDP recovery, renderer patch ownership, or tab injection lifecycle.

The current failure is downstream of successful tab insertion: native component identity/presentation, row enabled state, and mutation interaction are not sufficiently diagnosable and the controls are not usable on the real QAM surface.

Preserve the existing bounded CDP/session recovery and compare-and-restore cleanup behavior unless implementation discovers a concrete regression.

### 3.2 Native component discovery is currently too loose

Current `qam.js` finds a broad CommonUI object using:

```js
candidate[prop]?.contextType?._currentValue && Object.keys(candidate).length > 60
```

and then resolves Toggle/Slider using loose source fragments such as:

```js
source?.includes("ToggleField,fallback")
source?.includes('ToggleField",')

source?.includes("SliderField,fallback")
source?.includes('SliderField",')
```

PanelSection discovery is also broad: it finds one export containing `.PanelSection`, then chooses another export that does not contain `.PanelSection` as the row candidate.

A log line saying a component was "resolved" therefore proves only that one source-string heuristic matched. It does not prove the returned export is semantically the Steam control expected by our props.

### 3.3 Current presentation still recreates native layout details manually

Current code discovers Steam CSS-class modules for:

```text
QamTitleClass
FieldLabelRowClass
FieldLabelClass
FieldLabelValueClass
```

and manually builds slider label/value layout with:

```js
const labelRow = (label, value) =>
  React.createElement("div", {
    className: native.FieldLabelRowClass,
    style: {
      display: "flex",
      width: "100%",
      justifyContent: "space-between"
    }
  }, ...);
```

The Addon descriptor also adds its own wrapper spacing:

```js
style: { paddingTop: "16px" }
```

This is exactly the kind of copied presentation that should disappear if Steam's native fields already expose value/valueSuffix/notch props.

### 3.4 ToggleField is currently uncontrolled

Current renderer:

```js
React.createElement(native.ToggleField, {
  label: row.label,
  checked: quickSettingsRowEffectiveValue(row)?.booleanValue === true,
  disabled: !canMutateQuickSettingsRow(row),
  onChange: value =>
    void commitQuickSettingsImmediate(page, section, row, !!value),
});
```

It does not pass:

```js
controlled: true
```

Steam's Toggle implementation maintains its own internal checked state when not controlled. That means a failed/rejected mutation can visually diverge from the authoritative Addon page until another mount/state transition.

This omission alone is not sufficient evidence for the current "controls do not activate" failure, but it is incorrect for this authoritative snapshot model and must be fixed in the native rewrite.

### 3.5 Current Device QAM admission is duplicated surface policy

Current JavaScript keeps:

```js
const [deviceMutationAdmitted, setDeviceMutationAdmitted] = React.useState(false);

const nextDeviceMutationAdmitted =
  !!nextStatus &&
  nextStatus.steam?.active === true &&
  nextStatus.steam?.appId === 0 &&
  nextStatus.steam?.source === 1;
```

and gates every row through:

```js
const canMutateQuickSettingsRow = row =>
  !!row.available &&
  !!row.writable &&
  !busy &&
  (
    quickSettingsPage?.pageId !== QS_PAGE_DEVICE ||
    deviceMutationAdmitted
  );
```

The bridge independently repeats the same rule in:

```csharp
EnsureDeviceMutationAdmittedAsync(...)
```

requiring:

```text
Steam.Active == true
AppId == 0
Source == BigPicture
```

This is no longer the desired product policy.

### 3.6 Device runtime authority already resolves active-game precedence

The Device gate above is not the final safety authority.

Current Runtime already handles Device-vs-Profile precedence.

#### CPU Boost

A Device CPU mutation persists the Device setting, then `CpuBoostRuntime` resolves the current actual AppId. If the active game has an enabled game CPU policy, the game policy remains the effective Windows value.

Conceptually:

```text
edit Device CPU Boost
→ persist Device value
→ resolve actual AppId
→ active game CPU profile exists?
     yes -> apply/retain game value
     no  -> apply Device value
```

#### TDP

`TdpRuntime.CommitGlobalTdp(...)` persists the Device TDP, then calls:

```text
ResolveEffectiveTdp(updatedDocument, actualAppId)
```

which prefers an enabled valid active-game TDP profile before Device TDP.

#### Windows Power Mode

`PowerModeRuntime.ApplyEffective(...)` likewise checks the actual AppId and applies enabled game Power Mode before Device Power Mode.

Therefore the QAM-only "Big Picture + no game" Device mutation gate is duplicate surface policy, not the actual feature safety boundary.

The authoritative feature runtimes and `QuickSettingsMutationAdapter` remain responsible for persistence validity, row validity, model limits, target identity, active Profile precedence, and actual apply failure.

### 3.7 Current one-tab page selection remains in PR1

Current QAM chooses one page:

```js
const nextContext = activeGame
  ? { pageId: QS_PAGE_PROFILE, appId: nextAppId }
  : { pageId: QS_PAGE_DEVICE, appId: null };
```

Do not change this topology in QAM-NATIVE-01.

The later PR will replace one dynamic Addon tab with two stable Addon tabs.

### 3.8 Diagnostics stop before the useful failure boundary

Current QamHost logs installation and Steam console messages, but the normal product path does not expose enough information to distinguish:

```text
page loaded but all rows non-writable
native control resolved but wrong export
native control focused but onChange not fired
mutation callback fired but bridge request rejected
bridge request accepted but Runtime mutation failed
mutation succeeded but UI did not settle to authoritative result
```

`QamFrontendBridge` also collapses expected request exceptions to:

```text
Invalid or unavailable QAM bridge request.
```

Do not build a logging subsystem for this. Add only narrow low-noise diagnostics at the existing qam.js boundary.

---

## 4. Product policy frozen for PR1

### 4.1 Device settings are not disabled merely because a game is running

New QAM policy:

```text
Device page
→ same product availability/writability rules as the shared snapshot
→ no extra Big-Picture/no-game mutation admission
```

When Device is reachable, a row is editable if and only if the shared page says the row is available/writable and the local control is not temporarily busy.

Expected JS gate after this PR:

```js
const canMutateQuickSettingsRow = row =>
  !!row.available &&
  !!row.writable &&
  !busy;
```

The current one-tab PR1 UI normally still displays Profile while a game is active. Removing the Device gate now is intentional preparation for the next two-tab PR and removes duplicated policy immediately.

### 4.2 Profile target validity remains strict

Do not weaken Profile mutation validation.

The existing `QuickSettingsMutationAdapter` rules remain:

```text
Profile intent must carry AppId > 0
intent AppId must equal the current active Profile target
edited row must still be Available + Writable in a fresh projection
```

This is real stale-target protection and must remain.

### 4.3 No-game Profile presentation is NOT PR1

The next PR will keep both Device/Profile tabs mounted permanently and display a no-game Profile state such as:

```text
No game running
+ Profile controls visible but disabled/read-only
```

Do not implement that here.

Current PR1 retains the current one-tab selection:

```text
no game -> Device page
game    -> active Profile page
```

This keeps the interaction/native-control repair isolated from the tab-topology change.

---

## 5. Steam-native implementation reference

WSGM `2.0` currently pins:

```text
KillerPixelCrew/steam-ui-toolkit
13ce887fef7828ab56dd065b545b4747e6131880
```

The relevant toolkit does not guess a large CommonUI object and then scan it with broad names. It first identifies semantic webpack factories:

```js
const fieldsFactory = uniqueFactory([
  "DialogSlider_Container",
  "DropDownField",
  "SliderField",
]);

const layoutFactory = uniqueFactory([
  "PanelSectionTitle",
  "PanelSectionRow",
  "spinner",
]);
```

It then identifies controls from the exports of those known modules:

```js
const slider = uniqueFunction(fields, [
  "onChangeComplete",
  "notchCount",
  "valueSuffix",
  "explainerTitle",
]);

const toggle = uniqueFunction(fields, [
  "OnToggleChange",
  "this.Toggle()",
]);

const section = uniqueFunction(layout, [
  "PanelSectionTitle",
  "spinner",
]);

const row = uniqueObject(
  layout,
  value => value.$$typeof && typeof value.render === "function",
);
```

The toolkit also uses native field props directly:

```js
{
  label: "Battery charge limit",
  min,
  max,
  step,
  value,
  valueSuffix: "%",
  showValue: true,
  showBookendLabels: true,
  disabled,
  onChange,
}
```

and uses controlled native toggles:

```js
{
  checked: state.enabled,
  controlled: true,
  disabled,
  onChange,
}
```

For discrete/native-notch UI it uses:

```js
notchCount: values.length,
notchLabels: values.map((value, notchIndex) => ({
  notchIndex,
  label: `${value}`,
  value,
})),
notchTicksVisible: true,
```

Use these as implementation evidence, not as a dependency and not as a license to port unrelated WSGM architecture.

---

## 6. Required implementation

### 6.1 Replace broad native component discovery with semantic unique discovery

In `qam.js`, remove the current dependency on:

```text
findCommonUiModule(...)
isCommonUiModule(...)
findToggleField(commonUiModule)
findSliderField(commonUiModule)
findPanelComponents(...) broad fallback
findNativeClassStyles(...)
```

Replace it with a small deterministic semantic-discovery path modeled on the verified WSGM approach.

Suggested shape:

```js
function findUniqueFactory(webpackRequire, requiredTokens) {
  const matches = Object.entries(webpackRequire.m || {}).filter(([, factory]) => {
    const source = String(factory);
    return requiredTokens.every(token => source.includes(token));
  });
  return matches.length === 1 ? matches[0] : null;
}

function findUniqueFunction(exports, requiredTokens) {
  const matches = Object.values(exports || {}).filter(value => {
    if (typeof value !== "function") return false;
    const source = String(value);
    return requiredTokens.every(token => source.includes(token));
  });
  return matches.length === 1 ? matches[0] : null;
}

function findUniqueObject(exports, predicate) {
  const matches = Object.values(exports || {}).filter(
    value => value && typeof value === "object" && predicate(value)
  );
  return matches.length === 1 ? matches[0] : null;
}
```

Then:

```js
function findNativeQamComponents(webpackRequire) {
  const fieldsFactory = findUniqueFactory(webpackRequire, [
    "DialogSlider_Container",
    "DropDownField",
    "SliderField",
  ]);

  const layoutFactory = findUniqueFactory(webpackRequire, [
    "PanelSectionTitle",
    "PanelSectionRow",
    "spinner",
  ]);

  if (!fieldsFactory || !layoutFactory) {
    logOnce("nativeFactories", "QAM native fields/layout factory discovery failed.");
    return null;
  }

  const fields = webpackRequire(fieldsFactory[0]);
  const layout = webpackRequire(layoutFactory[0]);

  const SliderField = findUniqueFunction(fields, [
    "onChangeComplete",
    "notchCount",
    "valueSuffix",
    "explainerTitle",
  ]);

  const ToggleField = findUniqueFunction(fields, [
    "OnToggleChange",
    "this.Toggle()",
  ]);

  const PanelSection = findUniqueFunction(layout, [
    "PanelSectionTitle",
    "spinner",
  ]);

  const PanelSectionRow = findUniqueObject(
    layout,
    value => value.$$typeof && typeof value.render === "function"
  );

  if (!SliderField || !ToggleField || !PanelSection || !PanelSectionRow) {
    logOnce("nativeControls", "QAM required native controls/layout unavailable.");
    return null;
  }

  return { SliderField, ToggleField, PanelSection, PanelSectionRow };
}
```

Requirements:

- exactly-one match semantics;
- no first-random-match fallback;
- no DOM scraping fallback;
- no component-name dependency;
- fail closed if the Steam client no longer matches the known native component model;
- log which semantic stage failed;
- keep the existing `native-components` deterministic install failure classification.

Do not keep both old and new discovery systems "just in case". That increases ambiguity and makes the current false-positive problem harder to diagnose.

### 6.2 Stop copying Steam field-label CSS

Once native SliderField props are used correctly, delete the copied slider value layout path:

```text
FieldLabelRowClass
FieldLabelClass
FieldLabelValueClass
labelRow(...)
manual flex/space-between inline style
```

Also remove `findNativeClassStyles(...)` if it has no remaining legitimate caller.

Do not retain unused CSS-module discovery just because previous tests assert it.

### 6.3 Use native SliderField value presentation

For numeric rows, prefer native props:

```js
return React.createElement(native.SliderField, {
  label: row.label,
  min: row.sliderSpec.minimum,
  max: row.sliderSpec.maximum,
  step: row.sliderSpec.step || 1,
  value: numeric,
  valueSuffix: row.sliderSpec.suffix ?? "",
  showValue: true,
  showBookendLabels: true,
  disabled: !canMutateQuickSettingsRow(row),
  onChange: next =>
    scheduleQuickSettingsCommit(page, section, row, Number(next)),
});
```

Do not change the existing shared `row.commitPolicy` behavior in this PR.

The Addon already keeps a pending slider draft and forces a renderer pass, so native `value` should continue to follow the draft while the trailing commit is pending.

For discrete rows, use native notch labels rather than a custom label/value row:

```js
const notchLabels = options.map((option, notchIndex) => ({
  notchIndex,
  label: option.label,
  value: option.value,
}));

return React.createElement(native.SliderField, {
  label: row.label,
  min: 0,
  max: Math.max(0, options.length - 1),
  step: 1,
  value: optionIndex,
  notchCount: options.length,
  notchLabels,
  notchTicksVisible: true,
  disabled: !canMutateQuickSettingsRow(row),
  onChange: next => {
    const option = options[Math.round(Number(next))];
    if (option)
      scheduleQuickSettingsCommit(page, section, row, option.value);
  },
});
```

If the current Steam client proves one optional native display prop incompatible on-device, remove only that optional prop. Do not fall back to a second hand-built slider UI.

### 6.4 Make ToggleField authoritative/controlled

Required:

```js
return React.createElement(native.ToggleField, {
  label: row.label,
  checked: quickSettingsRowEffectiveValue(row)?.booleanValue === true,
  controlled: true,
  disabled: !canMutateQuickSettingsRow(row),
  onChange: value =>
    void commitQuickSettingsImmediate(page, section, row, !!value),
});
```

Authoritative rule:

```text
shared page / pending Addon draft
= rendered truth

Steam ToggleField internal click state
≠ independent truth
```

A failed bridge/runtime mutation must settle back to the authoritative returned page, not leave the switch showing an uncommitted click.

### 6.5 Remove the Device-specific QAM mutation admission

#### qam.js

Delete:

```text
deviceMutationAdmitted React state
nextDeviceMutationAdmitted calculation
setDeviceMutationAdmitted(...)
Device-admission pending-draft cancellation
Device settings unavailable message tied to that admission
Device special-case inside canMutateQuickSettingsRow(...)
```

Do not use `Steam.Active`, `Steam.Source`, or `AppId == 0` as a Device row edit gate.

The refresh still needs `captureStatus` in PR1 because it still chooses Device vs Profile in the current one-tab topology.

After cleanup, avoid keeping unused `status` React state if the captured status is only needed locally to resolve the page context.

Target:

```js
const canMutateQuickSettingsRow = row =>
  !!row.available &&
  !!row.writable &&
  !busy;
```

#### QamFrontendBridge.cs

Delete:

```csharp
EnsureDeviceMutationAdmittedAsync(...)
```

and simplify the page allow-list to only validate that QAM generic mutations are Device or Profile:

```csharp
switch (intent.PageId)
{
    case QuickSettingsPageId.Device:
    case QuickSettingsPageId.Profile:
        break;

    default:
        throw new InvalidOperationException(
            "Only Device/Profile Quick Settings mutation is available through the QAM generic seam.");
}
```

Do not move the old rule into Runtime, `QuickSettingsMutationAdapter`, shared presentation, or another helper.

The purpose is to remove the obsolete policy, not relocate it.

### 6.6 Keep existing shared Runtime validation

Do not weaken:

```text
QuickSettingsMutationIntent structural validation
Device intent AppId == null validation
Profile active-target AppId validation
fresh Available/Writable row validation
TDP group validation
TDP model/range validation
ProfileStore safe-replace policy
feature-runtime apply failure handling
shutdown admission
```

These are actual product correctness boundaries.

### 6.7 Add low-noise runtime diagnostics for the interaction boundary

The next real-device log must answer whether a control was genuinely usable and whether a mutation request fired.

Do not log every React render.

Add a small state-change dedupe helper, for example:

```js
function logStateChange(key, signature, message) {
  state.runtimeDiagnostics ??= {};
  if (state.runtimeDiagnostics[key] === signature) return;
  state.runtimeDiagnostics[key] = signature;
  log(message);
}
```

A fresh page should emit one useful summary when its meaningful state changes:

```text
QAM page state: Page=Device AppId=none Available=true Rows=9 AvailableRows=9 WritableRows=9
```

or:

```text
QAM page state: Page=Profile AppId=123456 Available=true Rows=8 AvailableRows=8 WritableRows=1
```

Mutation diagnostics should be emitted on actual RPC execution, not on slider motion before debounce:

```text
QAM mutation request: Page=Device AppId=none Row=DeviceCpuBoostEnabled
QAM mutation result: Page=Device AppId=none Row=DeviceCpuBoostEnabled Succeeded=true
```

Failure example:

```text
QAM mutation result: Page=Device AppId=none Row=DeviceTdpAcPl1 Succeeded=false Message=...
```

If a local mutation path returns early because the row is not writable, make that reason diagnosable without inventing a new state machine.

A small pure helper is acceptable:

```js
function quickSettingsRowMutationBlockReason(row, busy) {
  if (!row?.available) return "row-unavailable";
  if (!row?.writable) return "row-readonly";
  if (busy) return "busy";
  return null;
}
```

`canMutateQuickSettingsRow` may derive from it.

Do not add:

```text
logger manager
request tracing service
diagnostic event bus
ring buffer
epoch/barrier
retry state machine
```

The existing Steam console forwarding in `QamHost/Program.cs` is sufficient transport for these QAM diagnostics.

### 6.8 Preserve the current pending-draft and stale-context protections

Do not rewrite the existing generic pending scheduler just because slider rendering changes.

Keep:

```text
PageId + AppId pending identity
commit-group identity
whole-section TDP draft
linked-slider constraints from page metadata
trailing row.commitPolicy delay
context-change cancellation
fresh-page prune of absent/non-writable edited row
late-result same-context guard
self-invalidation deferral around an in-flight mutation
```

Those were introduced to solve real same-session stale-target/update problems and are independent from the native visual component cleanup.

After Device admission removal, delete only the admission-loss-specific cancellation branch.

### 6.9 Rename stale local identifiers only where it reduces confusion

`CpuBoostPanel` now renders the entire generic Device/Profile Quick Settings page.

Rename it to something accurate such as:

```text
QuickSettingsPanel
```

if the rename stays local to `qam.js`.

Do not turn this into a broad naming/refactor PR.

### 6.10 Do not change tab topology in PR1

Keep exactly one Addon tab descriptor in this PR.

Keep current descriptor identity and injection/restore ownership unless native-layout cleanup makes a presentation-only field unnecessary.

Do not add:

```text
steam-input-addon-device
steam-input-addon-profile
MenuStore default-selection logic
per-tab invalidation subscribers
no-game Profile placeholder projection
```

Those belong to QAM-TABS-02.

---

## 7. Tests to update

### 7.1 QamFrontendContractTests

Current tests intentionally lock several behaviors that PR1 must remove.

Delete/replace assertions requiring:

```text
QamTitleClass
FieldLabelRowClass
FieldLabelClass
FieldLabelValueClass
manual flex labelRow
paddingTop: "16px"
old CommonUIModule discovery heuristic
old ToggleField/fallback source markers
old SliderField/fallback source markers
deviceMutationAdmitted
Device settings unavailable
Device admission cancellation
```

Add contract assertions for:

```text
fields factory semantic tokens:
  DialogSlider_Container
  DropDownField
  SliderField

layout factory semantic tokens:
  PanelSectionTitle
  PanelSectionRow
  spinner

ToggleField semantic tokens:
  OnToggleChange
  this.Toggle()

SliderField semantic tokens:
  onChangeComplete
  notchCount
  valueSuffix
  explainerTitle

PanelSection semantic tokens:
  PanelSectionTitle
  spinner

controlled: true on native.ToggleField
numeric SliderField uses label/value/valueSuffix native props
numeric SliderField uses showValue
native discrete notchLabels
no copied FieldLabel CSS classes
no deviceMutationAdmitted
canMutateQuickSettingsRow = available + writable + !busy
```

Do not turn source-contract tests into a giant snapshot of exact formatting. Assert architecture-critical signatures only.

### 7.2 QamFrontendBridgeTests

Replace the old admission tests.

Remove tests whose required behavior is:

```text
Device allowed only in Big Picture with no game
Device rejected with a running game
Device rejected outside Big Picture
```

Add proof that Device generic mutation reaches Runtime regardless of the Steam status snapshot, for example:

```csharp
[Theory]
[InlineData(true, 0u, FrontendSteamSource.BigPicture)]
[InlineData(true, 480u, FrontendSteamSource.Actual)]
[InlineData(true, 480u, FrontendSteamSource.BigPicture)]
[InlineData(false, 0u, FrontendSteamSource.Actual)]
public async Task Generic_device_mutation_has_no_qam_specific_steam_admission(
    bool active,
    uint appId,
    FrontendSteamSource source)
{
    var (bridge, fake, server) = await StartAsync(new(active, appId, source));
    await using var _ = server;
    await using var __ = bridge;

    var response = await bridge.HandleRequestAsync(
        Request("mutateQuickSetting", CpuBoostToggleIntent()),
        CancellationToken.None);

    Assert.True(response.Ok);
    Assert.Equal(1, fake.MutateCount);
}
```

Keep:

```text
unknown page rejected
missing required identity rejected
malformed payload bounded error
Profile mutation reaches shared Runtime seam
capture path round trip
```

### 7.3 Shared mutation/runtime tests

Do not rewrite `QuickSettingsMutationAdapterTests` except where a direct regression assertion is genuinely needed.

Existing Device/Profile feature Runtime tests should remain green and remain the final proof that a Device edit during an active game does not bypass active-game precedence.

If a narrowly focused missing regression test is useful, add it at the feature Runtime layer rather than duplicating feature policy inside QAM tests.

---

## 8. Real-device acceptance test

Automated tests are not sufficient for this PR because the defect is on Steam's live native component surface.

Run on a real supported Claw with current Steam GamepadUI.

### 8.1 Big Picture / no game

```text
[ ] Open Steam Big Picture
[ ] Open QAM
[ ] Addon tab is present exactly once
[ ] Device page renders without "Device settings unavailable"
[ ] Steam controller navigation can focus Addon rows
[ ] keyboard navigation can focus Addon rows
[ ] CPU Boost toggle activates
[ ] Power Mode toggle activates where available
[ ] TDP toggle activates where available
[ ] numeric TDP slider moves
[ ] discrete CPU Boost / Power Mode sliders move
[ ] sliders show native Steam value/notch presentation
[ ] no custom copied label-row alignment artifact
```

### 8.2 Mutation proof

For one toggle and one numeric/discrete slider, verify the log contains the actual request/result boundary.

Expected shape:

```text
QAM page state: ... WritableRows=>0
QAM mutation request: ...
QAM mutation result: ... Succeeded=true
```

If the row visibly moves but no mutation request appears, investigate native callback wiring before considering the PR complete.

If the mutation request appears but result is rejected, use the authoritative result/failure to fix that concrete path.

### 8.3 Failure settlement

Force or simulate one typed mutation failure where practical.

Verify:

```text
[ ] controlled ToggleField does not remain visually flipped after authoritative failure
[ ] slider settles to the authoritative page returned by Runtime
[ ] failure text is visible/diagnosable
[ ] QAM does not crash/reload
```

### 8.4 Game running regression

Current PR1 topology still switches the one Addon panel to active Profile.

Verify:

```text
[ ] launch a Steam game
[ ] QAM Addon panel switches to Profile
[ ] Profile toggle/rows remain usable according to shared Available/Writable state
[ ] changing active game does not allow old-AppId pending work to mutate the new game
[ ] exiting the game returns the current one-tab surface to Device
```

Do not judge two-tab/default-selection UX in PR1. That belongs to PR2.

### 8.5 Teardown

```text
[ ] close/restart managed QamHost path as currently supported
[ ] Addon tab cleanup completes
[ ] no duplicate Addon tab after reinjection
[ ] existing compare-and-restore ownership tests remain green
```

---

## 9. Explicit non-goals

Do not implement any of the following in QAM-NATIVE-01:

```text
Device/Profile two-tab split
QAM default Device/Profile tab selection
Steam MenuStore selection
no-game Profile disabled placeholder
Profile "No game running" header
per-tab invalidation subscriber Set
new QuickSettings protocol fields
frontend/overlay protocol bump
new feature rows
FPS Limit exposure
Resolution exposure
new Device features
routing/controller lifecycle changes
HidHide changes
VIIPER changes
PID1901/PID1902 changes
OQ4 Overlay changes
polling
DOM click/focus simulation
Steam UI retry loops
new manager/service/registry abstractions
```

Do not add compatibility for unsupported multi-user/multi-session/RDP scenarios.

---

## 10. Full1902 lifecycle boundaries

This PR is frontend/native-QAM work.

It must not modify:

```text
PID1901 ↔ PID1902 ownership
Center M Enabled/Disabled authority
HidHide normalization/recovery
physical DirectInput ownership
VIIPER ownership/teardown
SteamDeck/Xbox360 presentation switching
Sleep/Hibernate/Resume controller handling
Restart/Crash/Shutdown controller recovery
PnP re-enumeration handling
routing rollback/fail-close
```

QAM frontend lifetime remains separate from Runtime lifetime:

```text
QAM closes
→ Runtime continues

QamHost reloads
→ Runtime continues
```

Do not accidentally couple Device feature persistence/runtime authority to QamHost lifetime while removing the surface admission rule.

---

## 11. Overengineering guard

The goal is not to defend against every theoretically possible React/async interleaving.

Keep the implementation small:

```text
one semantic Steam component discovery path
one shared generic renderer
one shared mutation seam
existing Runtime authorities
existing pending-draft scheduler
small state-change diagnostics
```

Do not add a new state machine because a synthetic timing test can be written.

Additional synchronization is justified only if a realistic supported lifecycle or normal Steam UI interaction demonstrates an actual stale mutation, incorrect state, crash, hang, or corruption path.

---

## 12. Expected production files

Primary expected edits:

```text
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs

tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
```

Possible narrow test-only edits:

```text
tests/SteamInputAddonforClaw.Tests/QuickSettingsMutationAdapterTests.cs
feature Runtime tests only if a missing active-game-precedence regression test is discovered
```

Unexpected production edits outside QamHost/shared Quick Settings should be treated as a review signal that scope is expanding.

---

## 13. Validation commands

At minimum:

```powershell
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj
```

Also build the relevant solution/projects using the repository's normal CI configuration.

No test should be weakened merely because it locks the old admission/native-discovery behavior. Replace obsolete assertions with assertions for the new contract.

---

## 14. Definition of done

QAM-NATIVE-01 is complete only when all of the following are true:

```text
[ ] QAM injection still succeeds on current Steam GamepadUI
[ ] Addon tab still inserts exactly once
[ ] native controls are resolved by semantic unique signatures
[ ] no broad CommonUI first-match/fallback discovery remains
[ ] no copied FieldLabel CSS layout remains
[ ] no manual 16px Addon wrapper spacing remains unless proven required by native layout
[ ] ToggleField is controlled
[ ] numeric slider uses native value/valueSuffix presentation
[ ] discrete slider uses native notch labels
[ ] Device QAM BigPicture/no-game admission is deleted from JS
[ ] Device QAM BigPicture/no-game admission is deleted from bridge
[ ] Device row edit gate is only Available + Writable + !busy
[ ] shared Runtime validation and active-game precedence remain unchanged
[ ] current one-tab Device/Profile selection remains unchanged
[ ] real QAM controller/keyboard interaction works
[ ] actual toggle mutation reaches bridge/runtime
[ ] actual slider mutation reaches bridge/runtime
[ ] success/failure settles to authoritative returned page
[ ] low-noise page/mutation diagnostics are visible in QamHost log
[ ] existing stale-context/pending-draft protections remain
[ ] teardown/reinjection remains clean
[ ] full test suite passes
```

---

## 15. Handoff to QAM-TABS-02

Do not pre-implement PR2 in this branch.

After QAM-NATIVE-01 is hardware-validated, the next PR should operate on this clean baseline and implement:

```text
stable Device tab   = always present
stable Profile tab  = always present

no game:
  Device normal
  Profile selectable but contents disabled
  Profile title/header = "No game running"
  QAM-open default = Device

game running:
  Device normal/editable
  Profile normal/editable for active AppId
  QAM-open default = Profile
```

The Profile tab must not be added/removed based on game lifecycle.

The later PR must also avoid fabricating stale last-game values for the no-game Profile state.

That topology/default-selection work is intentionally separate so a PR1 real-device failure can be attributed to native control integration rather than tab-selection behavior.
