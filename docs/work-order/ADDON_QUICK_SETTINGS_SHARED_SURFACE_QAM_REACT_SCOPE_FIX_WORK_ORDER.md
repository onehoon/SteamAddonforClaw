# Work Order — Fix QAM Shared-Tab React Scope Runtime Error

> **Date:** 2026-09-19  
> **Status:** Ready for implementation  
> **Reviewed production baseline:** `main` at `1e14c245e7b021910f11fbf0688d99f99d2f62cd`  
> **Observed hardware build:** `0.1.237.0`  
> **Scope:** QAM renderer-only follow-up after the shared five-tab convergence work  
> **CTW integration:** Out of scope

---

## 1. Goal

Fix the real-device Steam QAM runtime error that appears when navigating into the newly added shared inner-tab surfaces.

Observed Steam UI error:

```text
Shred SteamUI_11016093_3f1ce2a00fac50df
React is not defined
```

Hardware behavior:

- existing Device/Profile tabs render normally;
- the error is reproducible when entering newly added tabs;
- Setting reproduces the error directly;
- Shortcut has also surfaced the same Steam UI error during navigation;
- the controller/runtime path remains healthy in the same build.

This is a QAM renderer bug. It is not a Full1902 controller ownership, HidHide, VIIPER, presentation, Overlay, or transport redesign.

The implementation must make the smallest evidence-based fix and then validate all five QAM inner tabs on hardware.

---

## 2. Required reading before editing

Read:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md

docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR2_QAM_FIVE_TAB_NATIVE_SHELL_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR3_SETTING_TAB_ORDER_PARITY_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR4_SHORTCUT_PARITY_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR5_CONVERGENCE_CLEANUP_PARITY_ACCEPTANCE_WORK_ORDER.md
```

Then inspect current production source:

```text
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js

tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/AddonQuickSettingsSurfaceParityTests.cs
```

Do not modify unrelated Full1902 controller lifecycle code.

---

## 3. Confirmed production defect

Current `qam.js` defines `SettingTabOrderPanel` outside `buildAddonTab(React, native)`.

The component references `React` and `native` directly:

```javascript
function SettingTabOrderPanel({ tabOrderState, busy, error, onMove }) {
  if (!tabOrderState?.available) {
    return React.createElement(
      native.PanelSection,
      { title: "Tab Order" },
      React.createElement(
        native.PanelSectionRow,
        null,
        React.createElement(
          "p",
          null,
          error || "Tab order is unavailable.")));
  }

  // ...
  const rows = tabOrderState.rows.map((row, index) =>
    React.createElement(
      native.PanelSectionRow,
      // ...
      React.createElement(native.SliderField, {
        // ...
      })));
}
```

But its signature does not receive either dependency.

The current caller is:

```javascript
case AQS_TAB_SETTING:
  return React.createElement(
    SettingTabOrderPanel,
    settingProps);
```

Therefore `SettingTabOrderPanel` executes with free identifiers:

```text
React
native
```

that are not in that function's lexical scope.

This exactly explains the observed:

```text
React is not defined
```

ReferenceError.

---

## 4. Important comparison: Shortcut already uses the correct dependency-passing pattern

Do not assume Shortcut has the same source defect merely because hardware navigation can surface the same Steam error.

Current Shortcut code explicitly accepts the generation-scoped dependencies:

```javascript
function QuickSettingsShortcutPanel({ React, native, title }) {
  const [slots, setSlots] = React.useState(null);
  // ...
}
```

and the caller passes them:

```javascript
case AQS_TAB_SHORTCUT:
  return React.createElement(
    QuickSettingsShortcutPanel,
    { React, native, title: tab.label });
```

That path is structurally different from the broken Setting path.

Steam native `Tabs` may mount/evaluate content in a way that allows one broken tab component to surface an error while navigating among neighboring tabs. Current project sources do not prove the exact native Steam mounting strategy, so do not invent a second Shortcut fix without post-fix evidence.

The first implementation must fix the confirmed Setting dependency bug, then re-test Controller / Shortcut / Setting on hardware.

---

## 5. Required production change

Keep the current narrow explicit dependency-passing pattern.

Do not introduce a new renderer context, provider, registry, wrapper hierarchy, global React alias, or state manager.

Change `SettingTabOrderPanel` to receive the same generation-scoped `React` and `native` dependencies explicitly.

Target shape:

```javascript
function SettingTabOrderPanel({
  React,
  native,
  tabOrderState,
  busy,
  error,
  onMove,
}) {
  // existing rendering logic unchanged
}
```

Then pass those dependencies at the existing adapter boundary:

```javascript
case AQS_TAB_SETTING:
  return React.createElement(
    SettingTabOrderPanel,
    {
      React,
      native,
      ...settingProps,
    });
```

Equivalent formatting is fine.

The important invariant is:

```text
SettingTabOrderPanel uses React/native
→ React/native are explicit component inputs
→ no free React/native lookup
```

---

## 6. Do not move unrelated renderer ownership

The existing `buildAddonTab(React, native)` generation boundary remains authoritative for the resolved Steam React/native component set.

Current working patterns are acceptable:

```text
QuickSettingsPanel
-> nested inside buildAddonTab
-> captures React/native through closure

QuickSettingsShortcutPanel
-> top-level helper
-> receives React/native explicitly through props

SettingTabOrderPanel
-> top-level helper
-> MUST also receive React/native explicitly through props
```

Do not move every helper into `buildAddonTab` merely for stylistic uniformity.

Do not replace explicit dependencies with:

```javascript
window.React
globalThis.React
state.React
state.native
```

The Steam React/native set is generation-scoped and must stay tied to the currently resolved QAM generation.

---

## 7. Do not change autoFocusContents in this fix

Current QAM native Tabs call:

```javascript
return React.createElement(native.Tabs, {
  tabs: innerTabs,
  activeTab,
  onShowTab: setActiveTab,
  autoFocusContents: true,
});
```

Do not change `autoFocusContents` in this PR.

The observed error is an explicit JavaScript ReferenceError and the current source contains a direct scope bug that explains it.

Changing focus behavior before fixing the confirmed scope defect would mix two hypotheses and make hardware validation ambiguous.

After this fix:

- if Controller / Shortcut / Setting all render without the Steam error, stop;
- if Controller still has a separate focus/navigation failure, capture that independently and handle it in a focused follow-up.

No speculative focus workaround in this PR.

---

## 8. Preserve current product contracts and ownership

Do not change:

```text
AddonQuickSettingsTabId
AddonQuickSettingsShellContract
AddonQuickSettingsTabOrderContract
AddonQuickSettingsShortcutContract

QuickSettingsPageSnapshot
QuickSettingsMutationIntent
QuickSettingsMutationResult
```

Do not change:

- tab order persistence;
- `OverlayTabOrder` JSON key;
- QAM top-level Addon descriptor ownership;
- Device/Profile shared renderer;
- Shortcut product definition;
- Setting move semantics;
- Controller placeholder product scope.

Controller remains intentionally:

```text
shared shell identity
+ renderer-local placeholder
```

Do not invent Controller vibration / LED / mapping schemas in this fix.

---

## 9. No protocol changes

Expected protocol versions remain:

```text
FrontendTransportProtocol = 32
OverlayTransportProtocol  = 8
```

This fix is QAM renderer-local.

Do not bump either protocol.

Do not add a bridge method.

Do not add new Runtime state.

---

## 10. Full1902 / lifecycle non-goals

Do not touch:

- PID1901 / PID1902 transitions;
- Center M authority;
- HidHide applications or hidden-device ownership;
- DirectInput acquisition;
- VIIPER runtime ownership;
- Xbox360 / SteamDeck presentation selection;
- rumble ownership;
- OQ4 Overlay capture;
- Steam/BPM detector semantics;
- QamHost process lifecycle;
- restart / suspend / resume recovery.

The 0.1.237.0 hardware logs show those paths operating normally in the tested session.

This PR fixes a renderer ReferenceError only.

---

## 11. Regression tests

Update `QamFrontendContractTests.cs` with a focused regression guard.

At minimum prove both sides of the dependency seam.

### 11.1 Setting component declares explicit dependencies

Assert a shape equivalent to:

```javascript
function SettingTabOrderPanel({
  React,
  native,
  tabOrderState,
  busy,
  error,
  onMove
})
```

Do not merely assert that the source contains the words `React` and `native`; the test must guard that they are component inputs.

### 11.2 Setting adapter passes explicit dependencies

Assert a shape equivalent to:

```javascript
React.createElement(
  SettingTabOrderPanel,
  {
    React,
    native,
    ...settingProps,
  })
```

The exact source-string assertion may follow repository test style, but it must fail if the caller regresses to:

```javascript
React.createElement(SettingTabOrderPanel, settingProps)
```

### 11.3 Keep Shortcut's already-correct dependency seam guarded

Retain or strengthen the current Shortcut source guard so it proves:

```javascript
function QuickSettingsShortcutPanel({ React, native, title })
```

and:

```javascript
React.createElement(
  QuickSettingsShortcutPanel,
  { React, native, title: tab.label })
```

Do not create a generic JavaScript static-analysis framework for this.

A small source-contract regression test is sufficient for the current architecture.

---

## 12. Validation

Run:

```text
node --check src/SteamInputAddonforClaw.QamHost/Frontend/qam.js

dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj
```

If the repository's normal CI build/test command differs, run the existing CI-equivalent command as well.

Expected:

- JavaScript syntax check passes;
- all existing tests pass;
- new QAM dependency-scope regression test passes;
- no protocol snapshot/version change;
- no unrelated production file churn.

---

## 13. Required real-device acceptance

Use a normal supported Steam QAM session on the MSI Claw.

Navigate through all five Addon inner tabs:

```text
Device
→ Profile
→ Controller
→ Shortcut
→ Setting
→ Device
```

Verify:

### Device

- existing Device content still renders;
- existing controls remain usable.

### Profile

- existing Profile behavior is unchanged;
- no regression from the renderer fix.

### Controller

- placeholder renders;
- no Steam error page;
- no `React is not defined`.

### Shortcut

- four shared read-only slots render;
- no Steam error page;
- no `React is not defined`.

### Setting

- tab-order rows render;
- native sliders render;
- one-position move still works;
- authoritative order refreshes correctly;
- no Steam error page;
- no `React is not defined`.

Then close and reopen QAM once and repeat navigation.

Do not require pathological timing tests.

---

## 14. Logging interpretation

The 0.1.237.0 QamHost log can remain free of WARN/ERROR even while Steam displays this error.

Current QamHost console capture intentionally filters for Addon-prefixed messages:

```text
[SteamInputAddon:QAM]
```

A Steam/React runtime ReferenceError does not necessarily appear in that log.

Do not expand global Steam console/error logging as part of this fix unless the confirmed scope fix fails hardware acceptance and additional diagnostics become necessary.

---

## 15. Acceptance criteria

The PR is complete when all are true:

1. `SettingTabOrderPanel` has no free `React` or `native` dependency.
2. The Setting adapter explicitly supplies the current generation's `React` and `native`.
3. Shortcut keeps its existing explicit dependency seam.
4. Device/Profile renderer code is unchanged except where formatting/test adjacency requires it.
5. `autoFocusContents` remains unchanged.
6. Frontend protocol remains v32.
7. Overlay protocol remains v8.
8. Full1902/controller lifecycle files are untouched.
9. Automated tests pass.
10. Real hardware can navigate Device / Profile / Controller / Shortcut / Setting without the Steam `React is not defined` error.
11. Setting move behavior and Shortcut rendering remain functional after the fix.

---

## 16. Review guidance

Treat this as a narrow production bug fix.

Blocking findings should be limited to realistic issues such as:

- `React` / `native` still not explicitly available to Setting at render time;
- regression of Device/Profile/Shortcut/Setting behavior;
- new protocol or product authority introduced unnecessarily;
- accidental QAM lifecycle/controller-lifecycle changes;
- tests that do not actually guard the broken dependency seam.

Do not require new managers, providers, epochs, barriers, retries, or generalized renderer abstractions.

The desired result is a small, obvious fix to one confirmed runtime error.
