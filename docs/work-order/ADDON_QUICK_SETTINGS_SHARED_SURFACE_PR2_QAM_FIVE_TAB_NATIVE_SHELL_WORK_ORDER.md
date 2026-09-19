# Work Order — Addon Quick Settings Shared Surface PR2: QAM Five-Tab Native Inner Shell

> **Date:** 2026-09-19  
> **Status:** Ready for implementation  
> **Reviewed production baseline:** `main` at `f78311814b53c2061ff8d4c9dbcf0602a9fd9dae` after PR #527  
> **Architecture authority:** `docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md`  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and the documents it references  
> **Previous PR:** PR #527 — shared shell identity / Runtime contract foundation  
> **Scope:** Convert the current two Addon-owned Steam QAM top-level tabs into exactly one Addon top-level descriptor whose panel renders the Runtime-projected five-tab Addon Quick Settings shell using Steam's native inner `Tabs` component.

---

## 1. Goal

PR1 established one shared product identity and read seam:

```text
AddonQuickSettingsTabId
  Device
  Profile
  Controller
  Shortcut
  Setting

AddonQuickSettingsTabOrderContract
AddonQuickSettingsShellSnapshot
CaptureAddonQuickSettingsShellAsync
captureQuickSettingsShell
```

PR2 must make Steam QAM consume that contract.

Target:

```text
Steam native QAM
        |
        v
exactly one Addon-owned top-level tab
        |
        v
Steam native inner Tabs
        |
        +-- Device
        +-- Profile
        +-- Controller
        +-- Shortcut
        +-- Setting

inner identity/order/labels
        ^
        |
AddonQuickSettingsShellSnapshot
```

Device and Profile must keep using the existing generic `QuickSettingsPageSnapshot` renderer and mutation path. Controller, Shortcut, and Setting are shell-presence-only in this PR. Their functional parity is intentionally deferred:

```text
PR3 -> Setting parity
PR4 -> Shortcut parity
Controller -> placeholder until a real typed Controller product exists
```

This is a QAM composition/topology migration. It is not a Runtime ownership redesign.

---

## 2. Required documents and source to read first

Read together before editing:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md
docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md

docs/QAM_SINGLE_ADDON_TAB_NATIVE_INNER_TABS_RESEARCH_AND_DESIGN_2026-09-13.md

docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR1_FOUNDATION_WORK_ORDER.md
docs/work-order/QAM_NATIVE_02_DEVICE_PROFILE_STABLE_TABS_WORK_ORDER.md
```

Then inspect current production source:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs

src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw.QamHost/Program.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs

src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsInProcessSeamTests.cs
```

CTW integration is not part of this design. This is standalone Full1902 work.

---

## 3. Important baseline correction: current source still has two top-level Addon tabs

The shared-surface architecture describes the desired end state as one Addon top-level descriptor with native inner tabs. The actual reviewed `main` at `f783118...` still has the earlier two-top-level implementation.

Current `qam.js` contains:

```javascript
const ADDON_DEVICE_TAB_KEY = "steam-input-addon-device";
const ADDON_PROFILE_TAB_KEY = "steam-input-addon-profile";
```

and:

```javascript
const descriptors = {
  [ADDON_DEVICE_TAB_KEY]: buildAddonTab(React, native, QS_PAGE_DEVICE),
  [ADDON_PROFILE_TAB_KEY]: buildAddonTab(React, native, QS_PAGE_PROFILE),
};

const desired = [
  descriptors[ADDON_DEVICE_TAB_KEY],
  descriptors[ADDON_PROFILE_TAB_KEY],
];
```

So PR2 is not merely "add three more tabs".

Real migration:

```text
CURRENT
Steam QAM top level
+-- Device   (Addon descriptor)
+-- Profile  (Addon descriptor)

PR2 TARGET
Steam QAM top level
+-- Addon    (one Addon descriptor)
    +-- native inner Tabs
        +-- Device
        +-- Profile
        +-- Controller
        +-- Shortcut
        +-- Setting
```

Follow actual current source when it conflicts with stale wording in older notes.

---

## 4. PR1 foundation is complete — do not rebuild it

PR #527 already added:

```csharp
public enum AddonQuickSettingsTabId
{
    Device,
    Profile,
    Controller,
    Shortcut,
    Setting,
}
```

```csharp
public static class AddonQuickSettingsTabOrderContract
```

```csharp
public sealed record AddonQuickSettingsShellTab(
    AddonQuickSettingsTabId TabId,
    string Label);

public sealed record AddonQuickSettingsShellSnapshot(
    bool Available,
    IReadOnlyList<AddonQuickSettingsShellTab> Tabs);
```

Runtime already projects the authoritative order and labels:

```csharp
public Task<AddonQuickSettingsShellSnapshot> CaptureAddonQuickSettingsShellAsync(
    CancellationToken cancellationToken = default)
{
    ThrowIfShuttingDown();
    cancellationToken.ThrowIfCancellationRequested();

    return Task.FromResult(
        AddonQuickSettingsShellContract.Create(
            _settings.AddonQuickSettingsTabOrder));
}
```

The `.Qam` bridge already exposes:

```text
captureQuickSettingsShell
```

through:

```csharp
"captureQuickSettingsShell"
    => await _client.CaptureAddonQuickSettingsShellAsync(token),
```

Frontend protocol is already v31.

PR2 must NOT:

- add a second shell DTO;
- add another tab enum;
- add a QAM-specific tab-order setting;
- rename the persisted `OverlayTabOrder` JSON key;
- bump Frontend protocol merely because `qam.js` begins consuming an RPC that already exists;
- change Overlay protocol v7;
- create another Runtime shell owner.

---

## 5. Current QAM behaviors to preserve

### 5.1 Steam-native controls

Current native discovery resolves:

```text
SliderField
ToggleField
PanelSection
PanelSectionRow
```

Device/Profile rendering is intentionally Steam-native.

PR2 adds native `Tabs` discovery. It must not replace the existing controls with custom HTML/CSS.

### 5.2 Existing generic Device/Profile renderer

Current `QuickSettingsPanel({ pageId })` already consumes:

```text
captureQuickSettingsPage
mutateQuickSetting
```

and preserves:

- Runtime-owned row labels/order/options;
- Toggle vs Slider control kind;
- shared commit/debounce policy;
- grouped TDP mutation;
- linked slider constraints;
- pending slider draft identity;
- stale Profile AppId rejection;
- mutation-result page authority;
- state-invalidation refresh;
- Device/Profile context isolation.

Mount/reuse this implementation inside the new inner tabs. Do not fork it into separate Device/Profile product renderers.

### 5.3 Multi-subscriber invalidation already exists

Current code already has:

```javascript
state.stateInvalidationSubscribers ??= new Set();

function subscribeStateInvalidation(callback) {
  state.stateInvalidationSubscribers.add(callback);
  return () => state.stateInvalidationSubscribers?.delete(callback);
}
```

Do not add an event bus, frontend manager, page manager, or invalidation service.

### 5.4 Existing QAM-open lifecycle remains outside PR2 redesign

Current source uses:

```javascript
updateQamSurfaceVisibility(args[0]?.visible);
```

with:

```text
qamSurfaceActive
qamInitialSelectionRequested
activateQamSurface()
deactivateQamSurface()
trySelectAddonTabForFreshOpen()
```

PR2 changes the selected key but does not reopen the broader lifecycle design.

Do not replace it with polling, MutationObserver, DOM click/focus, synthetic input, retry loops, timeout loops, epochs, or another QAM lifecycle state machine.

---

## 6. Product identity rules

### 6.1 One current top-level key

Introduce exactly one current Addon top-level key:

```javascript
const ADDON_TAB_KEY = "steam-input-addon";
```

The top-level descriptor may use the fixed surface label `"Addon"` and the existing Addon icon. This is not a duplicate of the five shared inner-tab labels.

### 6.2 Old Device/Profile keys become cleanup-only

Keep the historical stable keys only to recognize stale Addon-owned descriptors:

```javascript
const LEGACY_ADDON_DEVICE_TAB_KEY = "steam-input-addon-device";
const LEGACY_ADDON_PROFILE_TAB_KEY = "steam-input-addon-profile";
```

Do not use them for new selection or rendering.

### 6.3 Ownership detection stays key/marker based

Never remove a descriptor because its visible title happens to be `Addon`, `Device`, or `Profile`.

---

## 7. Add Steam native inner `Tabs` discovery

Research already identified the generic Steam native Tabs primitive.

Known semantic evidence:

```text
.TabRowTabs
activeTab:
```

Controlled component shape:

```text
tabs
activeTab
onShowTab
autoFocusContents
```

Extend `findNativeQamComponents()` using the same unique-match / fail-closed discipline as the existing native controls.

Invariant:

```text
exactly one semantic native Tabs component
-> accept

zero matches
-> fail closed locally

multiple matches
-> fail closed locally
```

Do not broaden matching until "something works". Do not bind to Friends/Chat private stores. Do not add Decky/WSGM/steam-ui-toolkit as dependencies.

Illustrative shape:

```javascript
const tabsFactory = findUniqueFactory(webpackRequire, [
  ".TabRowTabs",
  "activeTab:",
]);

if (!tabsFactory) {
  logOnce(
    "nativeTabsFactory",
    "QAM native Tabs factory discovery failed (expected exactly one semantic match).");
  return null;
}

let tabsModule;
try {
  tabsModule = webpackRequire(tabsFactory[0]);
} catch (error) {
  logOnce(
    "nativeTabsExports",
    `QAM native Tabs module load failed: ${String(error)}`);
  return null;
}

// Resolve the exact generic controlled Tabs export from the verified current
// Steam module. Preserve unique-match behavior; do not accept multiple candidates.
const Tabs = /* uniquely resolved native Tabs export */;

if (!Tabs) {
  logOnce(
    "nativeTabs",
    "QAM native Tabs discovery failed (expected exactly one semantic match).");
  return null;
}
```

Then extend the current native result:

```javascript
return {
  SliderField,
  ToggleField,
  PanelSection,
  PanelSectionRow,
  Tabs,
};
```

No CSS tab fallback.

---

## 8. Replace two top-level descriptors with one generation-scoped descriptor

Current state uses:

```javascript
state.addonTabDescriptors ??= {};
```

Target:

```javascript
state.addonTabDescriptor
```

Exactly one current descriptor object may exist per script generation.

Conceptual shape:

```javascript
function buildAddonTab(React, native) {
  if (state.addonTabDescriptor) return state.addonTabDescriptor;

  state.addonTabDescriptor = {
    [TAB_MARKER]: ADDON_TAB_KEY,
    key: ADDON_TAB_KEY,
    title: "Addon",
    tab: buildAddonIcon(React),
    panel: React.createElement(AddonQuickSettingsPanel),
  };

  return state.addonTabDescriptor;
}
```

The icon may remain inline if extracting `buildAddonIcon()` would only add indirection.

The descriptor must still be cleared on install/reinstall/uninstall because it closes over generation-local React/native components.

---

## 9. Build five inner tabs from the shared shell snapshot

The new Addon panel must call:

```javascript
request("captureQuickSettingsShell")
```

The Runtime payload owns:

```text
inner tab sequence
inner tab labels
```

QAM may know how each known tab identity maps to its surface adapter. QAM must not own canonical order or labels.

### 9.1 Narrow JS validation

The bridge is dynamic JS, so fail closed on an unusable shell.

Require:

```text
Available == true
Tabs is an array
exactly five entries
every TabId is known
no duplicate TabId
every Label is a non-empty string
```

Do not repair an invalid shell into a locally invented default order. Do not persist a QAM shell cache.

Example:

```javascript
const AQS_TAB_DEVICE = 0;
const AQS_TAB_PROFILE = 1;
const AQS_TAB_CONTROLLER = 2;
const AQS_TAB_SHORTCUT = 3;
const AQS_TAB_SETTING = 4;

const KNOWN_AQS_TAB_IDS = new Set([
  AQS_TAB_DEVICE,
  AQS_TAB_PROFILE,
  AQS_TAB_CONTROLLER,
  AQS_TAB_SHORTCUT,
  AQS_TAB_SETTING,
]);

function validateQuickSettingsShell(shell) {
  if (shell?.available !== true ||
      !Array.isArray(shell.tabs) ||
      shell.tabs.length !== 5) {
    return null;
  }

  const seen = new Set();
  for (const tab of shell.tabs) {
    if (!KNOWN_AQS_TAB_IDS.has(tab?.tabId)) return null;
    if (seen.has(tab.tabId)) return null;
    if (typeof tab.label !== "string" || tab.label.length === 0) return null;
    seen.add(tab.tabId);
  }

  return shell.tabs;
}
```

Those numeric values mirror the closed C# enum ABI; they are not a duplicate order table.

Do not add a generic schema-validation framework.

---

## 10. Inner content mapping

Mapping:

```text
Device     -> existing QuickSettingsPanel(Device)
Profile    -> existing QuickSettingsPanel(Profile)
Controller -> temporary QAM placeholder
Shortcut   -> temporary QAM placeholder
Setting    -> temporary QAM placeholder
```

Example adapter:

```javascript
function buildInnerTabContent(React, native, tab, QuickSettingsPanel) {
  switch (tab.tabId) {
    case AQS_TAB_DEVICE:
      return React.createElement(
        QuickSettingsPanel,
        { pageId: QS_PAGE_DEVICE });

    case AQS_TAB_PROFILE:
      return React.createElement(
        QuickSettingsPanel,
        { pageId: QS_PAGE_PROFILE });

    case AQS_TAB_CONTROLLER:
    case AQS_TAB_SHORTCUT:
    case AQS_TAB_SETTING:
      return React.createElement(
        native.PanelSection,
        { title: tab.label },
        React.createElement(
          native.PanelSectionRow,
          null,
          React.createElement(
            "p",
            null,
            "This page is not available in QAM yet.")));

    default:
      return null;
  }
}
```

The temporary message is renderer-local. Do not invent Controller features, vibration/LED schemas, Shortcut action APIs, or Setting move controls in PR2.

---

## 11. Native Tabs must receive shared order and labels directly

Build the native list by mapping the validated payload in its existing order:

```javascript
const innerTabs = shellTabs.map(tab => ({
  id: String(tab.tabId),
  title: tab.label,
  content: buildInnerTabContent(
    React,
    native,
    tab,
    QuickSettingsPanel),
}));
```

Do not sort the payload.

Do not construct a local canonical array like:

```javascript
["Device", "Profile", "Controller", "Shortcut", "Setting"]
```

Do not append Device/Profile separately.

---

## 12. Inner selected-tab state is presentation-local only

Use one local React state, for example:

```javascript
const [activeTab, setActiveTab] = React.useState(null);
```

and feed the native controlled Tabs component:

```javascript
return React.createElement(native.Tabs, {
  tabs: innerTabs,
  activeTab,
  onShowTab: setActiveTab,
  autoFocusContents: true,
});
```

Exact props must match the verified current Steam component.

Do not persist selected inner tab. Do not send it to Runtime. Do not synchronize it with Overlay. Do not create a selected-tab service.

---

## 13. Preserve current context-based initial selection as INNER selection

Current two-top-level code selects Device/Profile according to `captureStatus`.

After PR2:

```text
top-level fresh-open selection
-> always ADDON_TAB_KEY

inner first selection
-> no active game: Device
-> active game: Profile
```

### 13.1 Top-level selection no longer needs AppId

Replace current Device/Profile key selection with:

```javascript
authority.set(ADDON_TAB_KEY);
```

No `captureStatus` call is needed merely to choose the top-level Addon key.

### 13.2 Inner initial selection uses status once

When the Addon panel gets a valid shell for the first time:

```text
capture shell
+ capture current status
-> choose Profile if AppId > 0
-> otherwise choose Device
```

Example:

```javascript
async function chooseInitialInnerTab(shellTabs) {
  const status = await request("captureStatus");
  const appId = Number(status?.steam?.appId || 0);
  const preferred =
    appId > 0 ? AQS_TAB_PROFILE : AQS_TAB_DEVICE;

  const match = shellTabs.find(
    tab => tab.tabId === preferred);

  return match ? String(match.tabId) : null;
}
```

After user selection:

```text
AppId changes
-> do not steal selected inner tab
-> existing Profile QuickSettingsPanel refreshes current target
```

Do not rerun initial selection on every state invalidation.

---

## 14. Keep current top-level open lifecycle; change only its target key

The current tested path uses:

```text
args[0]?.visible
qamSurfaceActive
qamInitialSelectionRequested
trySelectAddonTabForFreshOpen
MenuStore.OpenQuickAccessMenu(key, false)
```

Do not redesign it in PR2.

Update only the accepted key:

```javascript
function resolveNativeTabSelection() {
  const menuStore =
    window.SteamUIStore?.m_WindowStore?.m_Parent?.m_WindowStore
      ?.MainWindowInstance?.MenuStore;

  if (!menuStore ||
      typeof menuStore.OpenQuickAccessMenu !== "function") {
    return null;
  }

  return {
    set: key => {
      if (key !== ADDON_TAB_KEY) return;
      menuStore.OpenQuickAccessMenu(key, false);
    },
  };
}
```

`selectAddonTabForFreshOpen()` should perform the existing one-shot selection for `ADDON_TAB_KEY` only.

No retries.

---

## 15. Remove only state made obsolete by the topology change

Current generation state includes:

```text
addonTabDescriptor
addonTabDescriptors
qamSelectionContext
```

After one descriptor exists, `addonTabDescriptors` should disappear.

If `qamSelectionContext` only exists to carry the Device/Profile descriptor dictionary into fresh-open selection, remove it too.

Preferred direct state:

```text
addonTabDescriptor
qamSurfaceActive
qamInitialSelectionRequested
```

plus existing unrelated QAM state.

Do not replace removed state with a new manager/authority abstraction.

---

## 16. Top-level insertion and stale cleanup

`addonTabKey()` must recognize:

```text
legacy marker true
historical steam-input-addon-device
historical steam-input-addon-profile
current steam-input-addon
```

Example:

```javascript
function addonTabKey(tab) {
  const marker = tab?.[TAB_MARKER];

  if (marker === true) return "legacy";
  if (marker === ADDON_TAB_KEY) return ADDON_TAB_KEY;
  if (marker === LEGACY_ADDON_DEVICE_TAB_KEY)
    return LEGACY_ADDON_DEVICE_TAB_KEY;
  if (marker === LEGACY_ADDON_PROFILE_TAB_KEY)
    return LEGACY_ADDON_PROFILE_TAB_KEY;

  return null;
}
```

Then `ensureAddonTabs()` must preserve all unrelated Steam descriptors and end with exactly one current Addon descriptor:

```javascript
function ensureAddonTabs(owner, React, native) {
  const tabs = owner?.props?.tabs;
  if (!Array.isArray(tabs)) return null;

  const descriptor = buildAddonTab(React, native);
  const steamTabs = tabs.filter(tab => !addonTabKey(tab));
  const nextTabs = [...steamTabs, descriptor];

  const unchanged =
    tabs.length === nextTabs.length &&
    tabs.every((tab, index) => tab === nextTabs[index]);

  if (!unchanged)
    tabs.splice(0, tabs.length, ...nextTabs);

  trySelectAddonTabForFreshOpen();
  return tabs;
}
```

Keep current in-place array mutation unless current Steam evidence proves otherwise.

---

## 17. Reinjection / cleanup remains mandatory

PR #518's live-CEF rule still applies: descriptor objects closing over old React/native implementations must not survive into a new generation.

On new install generation:

```javascript
state.addonTabDescriptor = null;
```

On uninstall:

```javascript
state.addonTabDescriptor = null;
```

`restoreNestedPatches()` must remove every Addon-owned descriptor recognized by `addonTabKey()`, including historical Device/Profile keys and the new current key.

No persistent cache.

---

## 18. Shell refresh scope

PR2 establishes QAM composition from the shared shell. Do not add polling or another notification channel.

At minimum, every new Addon panel/script generation must capture current authoritative shell state.

PR3 owns QAM Setting mutation and authoritative tab-order mutation/readback parity.

If an existing event-driven invalidation can cheaply refresh shell state without new authority/state, reuse is allowed, but do not create infrastructure for it in PR2.

Core invariant:

> Every QAM shell build uses Runtime-projected order/labels; qam.js never owns a canonical five-tab product list.

---

## 19. Failure behavior

### Native Tabs resolution fails

```text
QAM integration fails closed locally
Runtime survives
Overlay survives
controller lifecycle unchanged
```

No custom tab fallback.

### Shell capture/validation fails

Do not fabricate a five-tab default in JS.

The one Addon panel may show a bounded local unavailable message, but it must not invent product order.

### Device/Profile page failure

Keep current page-local failure behavior. Do not tear down the inner shell, other page, or Runtime.

### Placeholder tabs

Controller/Shortcut/Setting are safe non-mutating placeholders in PR2.

---

## 20. Full1902 invariants — untouched

Do not change:

- Center M authority;
- PID1901/PID1902 policy;
- DirectInput ownership;
- HidHide state;
- VIIPER ownership;
- X360/SteamDeck presentation ownership;
- Steam/BPM presentation selection;
- PnP recovery;
- sleep/hibernate/resume;
- restart/shutdown teardown;
- OQ4 Overlay capture;
- WING/OEM1 routing;
- front-button mapping;
- Runtime lifetime.

QAM frontend state is never controller authority.

---

## 21. Overengineering guardrails

Do not create:

- `QamTabManager`;
- `QuickSettingsShellManager`;
- frontend composition service;
- generic page registry;
- plugin/provider model;
- JSON UI DSL;
- React/WinUI common component layer;
- selected-tab synchronization service;
- QAM selection epoch;
- lifecycle barrier/state machine;
- polling loop;
- WebView2 convergence;
- second transport;
- second settings authority.

Required architecture is small:

```text
existing Runtime authority
+ PR1 shell snapshot
+ existing .Qam bridge
+ one Addon descriptor
+ one native Tabs component
+ local React selected-tab state
+ existing QuickSettingsPanel
```

---

## 22. Expected production files

Primary production change:

```text
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
```

PR1 already supplied the C#/transport seam, so normally PR2 should NOT require production changes to:

```text
AddonQuickSettingsShellContracts.cs
FrontendWire.cs
NamedPipeAddonFrontendServer.cs
NamedPipeAddonFrontendClient.cs
QamFrontendBridge.cs
InProcessAddonFrontendControl.cs
SettingsStore.cs
StartupSettingsCoordinator.cs
OverlayWire.cs
OverlayWindow.xaml.cs
AddonProcessHost.cs
```

A very small correction is acceptable only if implementation proves an actual PR1 contract bug. Do not expand scope for adjacent cleanup.

---

## 23. Required test changes

Primary:

```text
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
```

Keep all PR1 bridge/transport tests green.
### 23.1 One current Addon top-level descriptor

Assert:

```text
ADDON_TAB_KEY = "steam-input-addon"
state.addonTabDescriptor
one descriptor appended by ensureAddonTabs
```

Current Device/Profile keys may remain only as historical cleanup constants.

### 23.2 Historical descriptor cleanup

Assert `addonTabKey()` recognizes:

- legacy marker;
- old Device key;
- old Profile key;
- new Addon key;

and unrelated Steam tabs remain untouched.

### 23.3 Native Tabs is mandatory/native

Assert:

- semantic native Tabs discovery exists;
- native Tabs comes from the resolver;
- no HTML/CSS tab clone;
- no DOM query/click fallback;
- no polling.

### 23.4 Shell snapshot drives inner tabs

Assert:

```javascript
request("captureQuickSettingsShell")
```

and that inner tabs map `shell.tabs`.

Prevent regression to:

```javascript
["Device", "Profile", "Controller", "Shortcut", "Setting"]
```

in qam.js.

Renderer ABI identity constants are allowed; canonical labels/order are not.

### 23.5 Device/Profile reuse generic renderer

Assert:

```text
Device TabId -> QS_PAGE_DEVICE -> QuickSettingsPanel
Profile TabId -> QS_PAGE_PROFILE -> QuickSettingsPanel
```

No second product renderer.

### 23.6 No fake APIs for placeholder pages

PR2 must not add:

- new `QuickSettingsPageId` values for Controller/Shortcut/Setting;
- QAM-only mutation methods;
- arbitrary action strings;
- fake controller/shortcut schema.

### 23.7 Top-level fresh-open selection uses one key

Update current native-selection tests:

```text
OpenQuickAccessMenu
-> ADDON_TAB_KEY only
```

Top-level selection no longer chooses Device/Profile from `captureStatus`.

### 23.8 Inner initial selection

Assert:

- active AppId -> Profile preferred initially;
- no active AppId -> Device preferred initially;
- `onShowTab` owns user-selected inner tab;
- ordinary invalidation/AppId changes do not force selection again.

### 23.9 Generation cleanup

If production removes `addonTabDescriptors`, remove obsolete test expectations and assert only `addonTabDescriptor` generation cleanup remains.

### 23.10 Keep existing generic-renderer regression tests

The current behaviors represented by these tests must stay intact:

```text
Qam_invalidation_subscribers_are_shared_but_each_panel_owns_its_refresh_and_pending_context
Qam_immediate_toggle_retires_same_section_same_context_pending_work_generically
Qam_device_and_profile_share_one_generic_renderer_driven_by_quick_settings_metadata
Qam_tdp_groups_use_shared_commit_group_and_a_whole_section_draft_for_either_page
Qam_device_and_profile_panels_use_fixed_page_identity_and_profile_tracks_active_app
Qam_context_identity_and_transition_are_pageid_and_appid_based
Qam_generic_prune_retires_a_pending_row_the_fresh_page_no_longer_allows
Qam_all_sliders_use_the_shared_trailing_commit_path_while_toggles_stay_immediate
Qam_pending_drafts_restore_after_remount_and_old_commit_responses_cannot_rewind_new_edits
Qam_invalidation_keeps_pending_drafts_and_never_clears_the_page_directly
Qam_mutation_result_page_is_authoritative_on_success_and_failure
```

Rename only topology-specific test names. Do not weaken coverage.

---

## 24. Bridge/transport regressions

Keep green:

```text
QamFrontendBridgeTests.Shared_shell_capture_round_trips_without_qam_admission
FrontendNamedPipeTransportTests.Shared_shell_round_trip_uses_the_runtime_snapshot
FrontendNamedPipeTransportTests.Shared_shell_capture_rejects_unexpected_payload_without_invoking_frontend
QuickSettingsInProcessSeamTests.Capture_shell_projects_the_runtime_tab_order_and_canonical_labels
```

Do not add a second QAM bridge method. Do not bump v31 unless the C# wire shape actually changes.

---

## 25. Suggested implementation sequence

1. Add native Tabs unique discovery.
2. Add one current `ADDON_TAB_KEY`; demote old keys to cleanup-only.
3. Make descriptor storage singular.
4. Preserve current `QuickSettingsPanel` implementation.
5. Add Addon shell panel:
   ```text
   captureQuickSettingsShell
   -> validate
   -> map payload order/labels to native inner tabs
   -> choose initial Device/Profile from status once
   -> render native Tabs
   ```
6. Convert `ensureAddonTabs()` to one descriptor.
7. Change fresh-open top-level selection to `ADDON_TAB_KEY`.
8. Remove directly obsolete two-descriptor state only.
9. Update focused QAM contract tests.
10. Run full CI-equivalent validation.
11. Perform manual Steam QAM acceptance.

Do not combine unrelated UI polish.

---

## 26. Build and automated validation

From repo root:

```powershell
dotnet restore SteamInputAddonforClaw.slnx

dotnet build SteamInputAddonforClaw.slnx `
  --no-restore `
  -v:minimal

dotnet test SteamInputAddonforClaw.slnx `
  --no-restore `
  --logger "console;verbosity=minimal"
```

Focused iteration:

```powershell
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj `
  --no-restore `
  --filter "FullyQualifiedName~QamFrontendContractTests|FullyQualifiedName~QamFrontendBridgeTests"
```

Focused tests do not replace the full suite before PR completion.

---

## 27. Manual QAM acceptance

Because this changes live Steam-native UI topology, automated source-contract tests are necessary but not sufficient for final product validation.

On a real MSI Claw with the supported current Steam GamepadUI:

### Topology

Verify exactly one Addon top-level QAM tab, not separate Device/Profile top-level tabs.

Inside Addon, verify five native inner tabs in the Runtime-provided order.

### Native UI

Verify:

- native tab-row appearance;
- native focus highlight;
- controller navigation;
- content focus transition;
- no custom web tab clone.

### Initial selection

```text
no active game
-> QAM opens on Addon
-> inner preferred tab = Device

active Steam game
-> QAM opens on Addon
-> inner preferred tab = Profile
```

Then manually switch inner tabs and verify later game start/exit does not steal the user's selected inner tab.

### Device/Profile

Verify current Toggle/Slider behavior, delayed commit, TDP linked behavior, Profile active-game changes, and no-game Profile unavailable state.

### Placeholders

Controller/Shortcut/Setting must exist in shared order and remain safe/non-mutating in PR2.

### Reopen/reinjection

Verify close/reopen and QamHost/script reinjection do not leave duplicate Addon descriptors or historical Device/Profile top-level descriptors.

---

## 28. Definition of done

- [ ] Baseline is PR #527 / `f783118...` or later rebased `main`.
- [ ] Exactly one current Addon top-level descriptor exists.
- [ ] Old Device/Profile descriptor keys are cleanup-only.
- [ ] No stale descriptor survives a script generation.
- [ ] Steam native generic `Tabs` is uniquely resolved and used.
- [ ] No CSS/DOM/input fallback tab implementation exists.
- [ ] QAM calls `captureQuickSettingsShell`.
- [ ] Five inner identities/order/labels come from the shared shell snapshot.
- [ ] qam.js contains no second canonical five-tab labels/order table.
- [ ] Device reuses existing generic Device Quick Settings renderer.
- [ ] Profile reuses existing generic Profile Quick Settings renderer.
- [ ] Controller/Shortcut/Setting are present without invented schemas.
- [ ] Top-level fresh-open selection targets only `ADDON_TAB_KEY`.
- [ ] Active-game context affects initial inner Device/Profile preference only.
- [ ] Later AppId changes do not steal selected inner tab.
- [ ] Existing pending/debounce/group/constraint behavior remains intact.
- [ ] Existing stale Profile AppId safeguards remain intact.
- [ ] Existing invalidation subscriber behavior remains intact.
- [ ] Frontend protocol remains v31 unless an actual wire change is necessary.
- [ ] Overlay protocol remains v7.
- [ ] Overlay visible behavior is unchanged.
- [ ] Full1902/HidHide/VIIPER/OQ4 lifecycle code is untouched.
- [ ] Focused QAM tests pass.
- [ ] Full test suite passes.
- [ ] Real Steam QAM topology/navigation acceptance is performed.

---

## 29. Review standard

Block only realistic production defects.

Blocking examples:

- two current Addon top-level descriptors remain;
- historical descriptors can duplicate after reinjection;
- shell labels/order are silently replaced by a QAM-local canonical copy;
- native Tabs discovery can accept an ambiguous component;
- Device/Profile generic renderer is forked or broken;
- user-selected inner tab is repeatedly stolen by ordinary AppId invalidation;
- stale Profile AppId mutation can reach Runtime;
- QAM failure affects controller/Runtime ownership;
- uninstall/reinstall leaves an old-generation descriptor alive.

Do not block for theoretical instruction-level races, speculative future Controller/Shortcut extensibility, or generic framework ideas.

The target is deliberately small:

> one Addon top-level descriptor, one Steam-native five-tab inner shell from Runtime truth, existing generic Device/Profile rendering, and no new authority.