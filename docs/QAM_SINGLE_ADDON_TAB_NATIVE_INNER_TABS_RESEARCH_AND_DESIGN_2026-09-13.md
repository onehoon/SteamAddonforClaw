# QAM Single Addon Top-Level Tab + Native Inner Tabs — Research and Design

> **Date:** 2026-09-13  
> **Status:** Research / design handoff  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Current implementation baseline:** `main` after PR #522 (`2f2f5ca63c658aa3531560e1f2dd5b9f492f2449`)  
> **Product scope:** Standalone Full PID1902 architecture. CTW integration is not part of this design.  
> **Purpose:** Consolidate the live-hardware findings from PR #522, the verified WSGM 2.0 / `steam-ui-toolkit` approach, Decky Loader / `decky-frontend-lib` native `Tabs` behavior, and the recommended next QAM topology.

---

## 1. Executive conclusion

The recommended QAM topology is now:

```text
Steam Quick Access Menu
│
├─ Steam native tabs...
│
└─ Addon                       <- exactly one Addon-owned top-level QAM tab
     │
     └─ Steam native inner Tabs
          ├─ Device
          │    └─ existing shared QuickSettingsPanel(Device)
          │
          └─ Profile
               └─ existing shared QuickSettingsPanel(Profile)
```

This is preferable to the current PR #521/#522 topology:

```text
Steam Quick Access Menu
├─ ...
├─ Device                      <- Addon top-level descriptor
└─ Profile                     <- Addon top-level descriptor
```

The key advantages are:

1. the Addon consumes only one top-level QAM slot;
2. Device/Profile becomes a controlled Addon-local UI selection instead of a Steam top-level selection problem;
3. the existing shared Device/Profile page authority, mutation safety, stale-AppId protection, delayed commit scheduler, and invalidation model can remain unchanged;
4. future small QAM pages can fit inside the Addon surface without consuming more vertical Steam QAM tabs;
5. the failed PR #522 `visible`-based fresh-open lifecycle state can likely be deleted rather than hardened with more state;
6. the design remains consistent with the project principle of one clear authority and avoiding defensive lifecycle state that does not protect a real supported product path.

The remaining independent requirement is:

> Every new QAM open should enter the single Addon top-level tab, regardless of Steam's remembered last-selected top-level tab.

That requirement should be solved separately from Device/Profile inner-tab state.

---

## 2. Full1902 architecture boundary

This QAM work is presentation-only.

The Full1902 architecture remains authoritative for controller ownership:

```text
Center M Enabled
-> MSI / Stock controller authority

Center M Disabled
-> Addon Runtime controller authority
-> PID1902 desired physical state
-> DirectInput / HidHide / VIIPER owned by Runtime
-> one live virtual presentation
```

Frontend lifetime remains separate from Runtime lifetime:

```text
Main UI closes
QAM closes
No frontend is visible

=> controller Runtime continues
```

Therefore this QAM redesign must not introduce a new controller authority, controller lifecycle owner, routing owner, or teardown path.

It must not modify:

- PID1901 / PID1902 ownership policy;
- HidHide authority or recovery;
- VIIPER ownership/teardown;
- Xbox360 / SteamDeck presentation switching;
- suspend / resume controller recovery;
- physical input recovery;
- Center M authority transitions.

---

## 3. Current QAM baseline after PR #522

PR #521 established two stable Addon top-level descriptors:

```js
const ADDON_DEVICE_TAB_KEY = "steam-input-addon-device";
const ADDON_PROFILE_TAB_KEY = "steam-input-addon-profile";
```

Current `qam.js`:

- discovers Steam GamepadUI / QAM through webpack semantic discovery;
- discovers native `PanelSection`, `PanelSectionRow`, `ToggleField`, and `SliderField`;
- injects two Addon-owned top-level tab descriptors;
- keeps Device and Profile as fixed page identities;
- uses a multi-subscriber invalidation `Set` for independently mounted Device/Profile views;
- keeps the generic shared Quick Settings projection/mutation contract;
- retains Profile active-AppId safety;
- retains pending slider/TDP commit identities by `(PageId, AppId)`;
- preserves the PR #518 generation cleanup rule for cached descriptors.

PR #522 then attempted to override Steam's remembered top-level tab on every fresh QAM open.

The implementation added conceptually:

```text
args[0].visible
-> qamSurfaceActive
-> qamInitialSelectionRequested
-> captureStatus
-> Device/Profile top-level key
-> MenuStore.OpenQuickAccessMenu(key, false)
```

The actual `MenuStore` resolver currently uses:

```js
window.SteamUIStore
  ?.m_WindowStore
  ?.m_Parent
  ?.m_WindowStore
  ?.MainWindowInstance
  ?.MenuStore
```

and the selection write is:

```js
menuStore.OpenQuickAccessMenu(key, false);
```

That selection path was based on live inspection showing the current Steam native tab handler passes its descriptor key to `OpenQuickAccessMenu`.

The failure observed on hardware is not sufficient evidence that this `MenuStore` selection API itself is wrong, because PR #522 never reached the open-time selection path during the reproduced opens.

---

## 4. PR #522 real-device failure — confirmed behavior

Real-device test after merging/building PR #522 showed:

```text
1. Device and Profile top-level tabs are both present.
2. Their content is usable.
3. If QAM is closed while a Steam-native tab is selected,
   reopening QAM shows the same Steam-native tab.
4. If QAM is closed while an Addon tab is selected,
   reopening QAM shows the same Addon tab.
```

This means Steam's normal remembered-last-tab behavior is still authoritative.

The 2026-09-13 logs establish an important separation:

### Working

- QamHost injection;
- native component discovery;
- nested tabs producer discovery;
- `props.tabs` owner discovery;
- Addon Device/Profile descriptor injection;
- OEM1 -> `SteamQuickAccess` action dispatch;
- virtual SteamDeck Quick Access pulse;
- QAM opening itself.

### Not observed at all during the repeated QAM opens

```text
QAM surface activated.
QAM surface deactivated.
QAM open selection: Device ...
QAM open selection: Profile ...
```

Therefore the PR #522 assumption:

```text
outer QAM renderer args[0].visible
false -> true == fresh QAM open
true -> false == QAM close
```

is not valid for the supported Windows GamepadUI execution path as currently patched.

The failure occurs before the open-time Addon selection write.

### Consequence

Do not interpret this hardware result as proof that:

```js
MenuStore.OpenQuickAccessMenu(addonKey, false)
```

cannot select an injected Addon tab.

That method was not reached from the reproduced fresh-open path.

The immediate failed authority is the `args[0].visible` lifecycle assumption.

---

## 5. What WSGM 2.0 actually does

WSGM branch `2.0` uses `KillerPixelCrew/steam-ui-toolkit` as a submodule.

The examined pinned revision is:

```text
13ce887fef7828ab56dd065b545b4747e6131880
```

The important correction is:

> WSGM 2.0 does not solve the same "fresh-open -> custom Addon top-level tab" problem that PR #522 tries to solve.

WSGM largely avoids that problem through a different topology.

### 5.1 WSGM reuses Steam native surfaces

The pinned `steam-ui-toolkit` discovers native controls/components semantically instead of relying on minified export names.

Examples include semantic token groups for:

```text
DialogSlider_Container
DropDownField
SliderField

PanelSectionTitle
PanelSectionRow
spinner
```

and native control fingerprints such as:

```text
SliderField:
  onChangeComplete
  notchCount
  valueSuffix
  explainerTitle

ToggleField:
  OnToggleChange
  this.Toggle()
```

This is the same broad design philosophy already used by our `qam.js`.

### 5.2 WSGM's native-panel patch model

The examined toolkit intercepts `React.useMemo`, observes Steam's memoized tab array, identifies exact target panel roots, and replaces only the target descriptor's `panel` with a wrapper.

Conceptually:

```js
const value = originalUseMemo(factory, dependencies);
if (!Array.isArray(value)) return value;

for (const wrapper of wrappers) {
    const matches = value.filter(item =>
        item &&
        React.isValidElement(item.panel) &&
        wrapper.match(item.panel.type));

    if (matches.length !== 1) continue;

    // preserve descriptor, replace only panel with wrapped panel
}
```

For example:

- the Performance panel can be matched by exact resolved root identity;
- the Quick Settings panel is a local function and is matched using stable Valve semantic strings in its function source.

The implementation deliberately requires one exact match before modifying a target.

### 5.3 Why WSGM does not need our Device/Profile fresh-open selection architecture

WSGM does not create the equivalent of:

```text
custom Device top-level tab
custom Profile top-level tab
```

and then try to force one of them selected at QAM open.

Instead, its controls live inside existing Steam-native panel surfaces.

Therefore WSGM has no need for the PR #522 concepts:

```text
qamSurfaceActive
qamInitialSelectionRequested
fresh-open AppId-based top-level key selection
```

This is important because it means PR #522 should not be considered a faithful WSGM lifecycle pattern that merely needs another small fix.

The useful WSGM lessons are:

- semantic discovery;
- stable Steam-shaped UI reuse;
- exact-match/fail-closed behavior;
- preserving native descriptor ownership where possible;
- avoiding DOM-level UI simulation.

---

## 6. Decky Loader `args[0].visible` — what it actually proves

Decky Loader's current `tabs-hook.tsx` does use a pattern similar to:

```tsx
(args, ret) => {
    const tabs = findInReactTree(ret, x => x?.props?.tabs);
    this.render(tabs.props.tabs, args[0].visible);
    return ret;
}
```

However Decky uses that `visible` value to propagate QAM visibility to injected plugin content:

```text
initialVisibility
qAMVisibilitySetter(visible)
```

Its `QuickAccessVisibleStateProvider` stores that value in React state and exposes it to plugin content.

Decky does not demonstrate that:

```text
args[0].visible transition
== authoritative fresh-QAM-open event
== correct ordering point to override Steam's remembered selected tab
```

PR #522 extended the meaning of the Decky value beyond what that source proves.

The 2026-09-13 hardware log shows that this extension is not valid in our supported Windows GamepadUI path.

---

## 7. Steam native generic inner `Tabs` is reusable

Decky's `decky-frontend-lib` exposes Steam's generic native `Tabs` component.

The current resolver finds a Steam module using semantic function content:

```text
.TabRowTabs
activeTab:
```

The exposed contract is controlled and simple:

```ts
interface TabsProps {
  tabs: Tab[];
  activeTab: string;
  onShowTab: (tab: string) => void;
  autoFocusContents?: boolean;
}
```

Each tab has conceptually:

```ts
interface Tab {
  id: string;
  title: string;
  content: ReactNode;
  renderTabAddon?: () => ReactNode;
  footer?: FooterLegendProps;
}
```

This is a generic Steam primitive used by Steam pages such as Library/Media-style inner tabs, not a Friends-store-specific component.

### 7.1 Relevant 2026 Steam Beta change

On 2026-03-22, Decky updated its latest-beta resolver by changing only the component fingerprint from:

```text
((function()
```

to:

```text
(function()
```

The public controlled component contract remained:

```text
tabs
activeTab
onShowTab
autoFocusContents
```

That is good evidence that the generic native `Tabs` primitive remains a practical reuse target, although any implementation in this project must still fail closed if current Steam discovery no longer resolves uniquely.

### 7.2 Friends/Chat is UX evidence, not an implementation dependency

Steam's Friends/Chat QAM surface is useful evidence that a nested/secondary-tab UX is native and acceptable inside QAM. However, the current research did **not** directly prove from Valve's current minified implementation that the Friends panel literally uses the exact same exported `Tabs` component resolved above.

Therefore the implementation rule is:

```text
use the generic Steam native Tabs primitive because its contract is independently resolved
!=
bind to Friends/Chat private component identity or stores
```

If later live inspection proves Friends uses the same primitive, that is additional confirmation only. The Addon design must not depend on that private implementation detail.

---

## 8. Recommended new topology

Replace the two Addon-owned top-level descriptors with one descriptor.

Suggested stable identities:

```js
const ADDON_TAB_KEY = "steam-input-addon";
const ADDON_INNER_DEVICE_TAB_ID = "device";
const ADDON_INNER_PROFILE_TAB_ID = "profile";
```

The exact top-level key can be chosen during implementation, but it must be one stable Addon-owned key and must not collide with stale legacy descriptors from older generations.

Target:

```text
Steam props.tabs
└─ one Addon descriptor
     └─ AddonPanel
          └─ native Steam Tabs
               ├─ Device
               └─ Profile
```

### 8.1 Inner tab state is Addon presentation state only

The native `Tabs` component should be controlled locally:

```text
active inner tab id
+ onShowTab(id)
```

This state must not become a new product authority.

It does not own:

- Device policy;
- Profile policy;
- profile persistence;
- current active AppId authority;
- feature availability/writability;
- mutation validation.

Those remain in the shared frontend model / Runtime owners.

### 8.2 Existing `QuickSettingsPanel({ pageId })` can remain

The current QAM content split is already suitable:

```text
Device panel
  PageId = Device
  AppId = null

Profile panel
  PageId = Profile
  AppId = current active Steam AppId or null
```

The new inner tabs should mount/reuse those existing panels rather than rebuilding Device/Profile presentation logic.

### 8.3 Preserve existing mutation safety

The redesign must retain:

- shared page payload as product authority;
- row Available/Writable checks;
- `(PageId, AppId)` pending identity;
- delayed slider/TDP grouping and commit policy;
- linked constraints;
- mutation-depth invalidation deferral;
- stale Profile AppId retirement;
- late-result guards;
- explicit no-game Profile unavailable state;
- Device pending work independence from Profile target changes.

This should be a topology/view composition change, not a shared frontend rewrite.

---

## 9. Recommended native `Tabs` discovery

Do not add Decky or WSGM as a runtime dependency.

Implement an independent semantic resolver in the same style as the current QAM native control discovery.

Reference signature:

```text
factory/export source contains:
  .TabRowTabs
  activeTab:
```

Then identify the exact exported component using the current Steam shape, with unique-match/fail-closed behavior.

The final implementation must not silently accept multiple matches.

Desired rule:

```text
unique native Tabs match
-> use native inner Tabs

zero or multiple matches
-> do not invent a CSS tab clone
-> report QAM native inner Tabs unavailable
```

For a production migration, whether failure should leave the single Addon descriptor with a simple static/unavailable panel or leave the prior topology temporarily intact should be decided explicitly in the implementation work order. Do not create two parallel topology authorities indefinitely.

---

## 10. Fresh-open requirement after the topology change

The topology change simplifies, but does not by itself eliminate, the top-level fresh-open requirement.

The requirement becomes:

```text
Every fresh QAM open
-> select exactly one known key:
   steam-input-addon
```

No AppId lookup is required to choose a top-level Addon key.

This is much simpler than PR #522:

```text
fresh open
-> detect AppId
-> choose Device or Profile top-level descriptor
-> select that key
```

Device/Profile can now be selected entirely inside the Addon panel.

---

## 11. Do not build another guessed QAM-open lifecycle

The failed PR #522 code should not be replaced by another broad candidate search such as:

```text
visible
isVisible
open
isOpen
active
isActive
focused
isFocused
```

nor by:

- DOM queries/clicks;
- MutationObserver;
- synthetic input;
- focus tricks;
- setInterval polling;
- repeated setTimeout retries;
- render-until-it-sticks selection writes;
- owner epochs/generations;
- a new generic QAM lifecycle manager.

The real next question is not "which other prop looks like open?".

It is:

> What exact Steam native operation/path establishes the selected QAM top-level key when a closed QAM is opened, and can that one open-time selection be deterministically changed to the Addon key?

---

## 12. Recommended minimal diagnostic PoC for top-level selection

Before implementing another fresh-open fix, instrument the exact `MenuStore.OpenQuickAccessMenu` call pattern on the supported current Steam build.

The current code already resolved:

```js
MainWindowInstance.MenuStore.OpenQuickAccessMenu
```

The next PoC should temporarily observe only bounded information:

```text
call occurred
key argument
second argument
whether QAM was invoked through the expected OEM1 test sequence
```

Do not dump the whole MenuStore or Steam state graph.

Test sequence:

### Case A — QAM closed, OEM1 pressed

Record whether `OpenQuickAccessMenu` is called and with which key.

### Case B — QAM open, user selects another Steam native top-level tab

Record the call/key pattern.

### Case C — QAM open, user selects the Addon top-level tab

Record the call/key pattern.

### Case D — QAM open, OEM1 pressed to close

Determine whether `OpenQuickAccessMenu` participates in close/toggle behavior or whether closure is handled through another path.

The objective is to distinguish:

```text
fresh open selection call
```

from:

```text
manual top-level tab navigation call
```

without inventing an unrelated lifecycle state machine.

### 12.1 Additional direct-call PoC before changing topology

There is a smaller question that can be answered on the **current two-top-level-tab build** before the single-tab migration is implemented:

> Can `OpenQuickAccessMenu("steam-input-addon-device", false)` itself open a closed QAM and select an injected string-key tab?

PR #522 did not answer this because the call was never reached during the reproduced fresh opens.

Test this directly from the already-connected QamHost/CDP context:

```text
Case E — cold first open
Steam/BPM session active
QAM has not been manually opened yet
-> invoke OpenQuickAccessMenu("steam-input-addon-device", false)

Case F — warm after descriptor insertion
open QAM manually once so the injected descriptor is known to have existed in props.tabs
close QAM
-> invoke the same direct call

Case G — QAM already open on a Steam-native tab
-> invoke the same direct call
-> verify whether it switches to Device without side effects

Case H — QAM already open on Device
-> invoke the same direct call again
-> determine whether it closes, stays open, or only reselects Device
```

This explicitly separates two unresolved questions:

```text
1. Can MenuStore open/select an injected string key at all?
2. Must that descriptor already exist in Steam's current tab array before the call?
```

The second question matters because an initial closed-QAM call may occur before the renderer has produced the tab array into which the Addon descriptor is inserted.

Interpretation:

```text
Case E PASS + Case F PASS
-> direct open/select is a real candidate

Case E FAIL + Case F PASS
-> likely descriptor-ordering/bootstrap constraint
-> do not misclassify this as a bad string-key contract

Case E FAIL + Case F FAIL
-> direct open/select is not currently proven viable
-> retain pulse/open authority and investigate the native selection seam
```

Also validate focus, Steam menu sound/haptic behavior, and the close/toggle result. A method that visually opens the correct tab but loses expected native input/focus behavior is not automatically a valid replacement for the system-button pulse.

---

## 13. Important OEM1 / Quick Access toggle constraint

Current Runtime behavior for `SteamQuickAccess` is intentionally routed through the live SteamDeck virtual presentation's Quick Access system-button pulse.

Conceptually:

```text
OEM1
-> FrontButtonAction.SteamQuickAccess
-> TryRequestQuickAccessPulse()
-> SteamDeck Quick Access system button pulse
```

That path naturally has a toggle-style user expectation:

```text
QAM closed -> press -> open
QAM open   -> press -> close
```

Do not immediately replace this Runtime system-button pulse with a blind direct call to:

```js
MenuStore.OpenQuickAccessMenu(ADDON_TAB_KEY, false)
```

because it is not yet proven that repeated calls preserve the same close/toggle behavior.

Preferred initial architecture if the call-pattern PoC supports it:

```text
Quick Access pulse
= remains the open/close authority

QAM native/MenuStore hook
= only overrides the fresh-open selected top-level key to Addon
```

This avoids changing the existing front-button controller presentation path just to solve a QAM view-selection issue.

Only if the PoC proves the native open path cannot be distinguished should a Runtime -> QamHost explicit open/select command be considered.

Do not add that IPC preemptively.

### 13.1 Additional candidate if direct open/select is proven

The recommendation above remains the conservative baseline because the existing SteamDeck Quick Access pulse already provides correct open/close behavior on hardware.

However, there is a potentially simpler architecture that should remain explicitly on the table **if Case E/F/H proves the native method has the required semantics**:

```text
OEM1 SteamQuickAccess action
-> one explicit QamHost request
-> MenuStore.OpenQuickAccessMenu(ADDON_TAB_KEY, false)
-> QAM opens directly on Addon
```

If this operation both:

- opens a closed QAM on the injected Addon key; and
- preserves the required repeat-press close/toggle/focus behavior,

then the application may not need to detect a fresh QAM open at all. In that proven case the direct operation is simpler than:

```text
pulse QAM open
-> observe/guess an open lifecycle
-> later override selected key
```

This is intentionally an **additional candidate**, not a replacement conclusion yet. The existing section 13 preference remains authoritative until live PoC evidence proves the direct operation has equivalent user-visible semantics.

### 13.2 If an explicit Runtime -> QamHost request is eventually chosen, reuse the existing `.Qam` transport

Do not create a new IPC channel, broker, service, or generic command bus for this one action.

The current architecture already has:

```text
Addon Runtime
-> dedicated CreateQamForCurrentUser() named-pipe server
-> QamFrontendBridge / NamedPipeAddonFrontendClient
-> QamHost
-> current Steam CDP session
```

The frontend transport also already supports Runtime-to-client notifications such as `StateInvalidated` and `CloseRequested`. Therefore, if a direct open/select request becomes the chosen design, the narrow shape should be conceptually equivalent to one QAM-specific notification/event, for example:

```text
OpenAddonQuickAccessRequested
```

rather than another transport subsystem.

There is one lifecycle constraint to retain: `QamHostProcessController` currently desires QamHost only while Big Picture or an actual Steam game is active. That matches the current `SteamQuickAccess` product domain, where the SteamDeck presentation is active. If a future product requirement wants this direct QAM operation outside that domain, QamHost lifetime would need separate review; do not silently broaden it as part of this work.

### 13.3 Explicit-request + existing pulse is a valid fallback if descriptor ordering blocks direct closed-QAM open

If the direct call fails only because the custom descriptor does not yet exist before the first QAM render, do **not** return to `args[0].visible` or another guessed open prop.

A smaller causality-based fallback is possible:

```text
OEM1 press is already known by Runtime
-> send one narrow "next QAM open should select Addon" request to QamHost/JS
-> issue the existing SteamDeck Quick Access pulse
-> when the QAM tab array is produced and the Addon descriptor is present
-> consume that one pending request
-> OpenQuickAccessMenu(ADDON_TAB_KEY, false) exactly once
```

The important difference from PR #522 is:

```text
PR #522
observed prop -> infer that a fresh open happened

explicit-request fallback
known OEM1 action -> intentionally arm one selection for the open that action is about to cause
```

This does not require:

- visibility inference;
- owner/session identity;
- epochs;
- timers;
- polling;
- repeated selection writes.

If implemented, the pending fact must remain narrowly scoped and be retired after consumption or document/session replacement. Do not generalize it into a QAM lifecycle state machine.

---

## 14. Inner Device/Profile initial-selection policy

Top-level and inner-tab policy should be separated.

Mandatory top-level behavior:

```text
fresh QAM open
-> Addon top-level tab
```

Inner Device/Profile selection can remain simple presentation state.

A reasonable initial policy is:

```text
first AddonPanel mount:
  no active game -> Device
  active game    -> Profile
```

After that, while the Addon panel remains mounted:

```text
user selects Device/Profile
-> respect that selection

active AppId changes
-> do not force inner tab change
-> Profile content refreshes to the current target
```

Do not reintroduce a fresh-QAM-open lifecycle solely to force Device/Profile every time the QAM is reopened unless real product testing demonstrates that requirement is necessary.

The user's primary UX requirement is that QAM opens on the Addon surface, not that the inner tab must always be forcibly reset after every hide/show.

---

## 15. What should be deleted if the new topology is adopted

Once the single-top-level topology and a verified fresh-open native selection seam are proven, the following PR #522 state is expected to become unnecessary:

```text
qamSurfaceActive
qamInitialSelectionRequested
qamSelectionContext
updateQamSurfaceVisibility()
activateQamSurface()
deactivateQamSurface()
trySelectAddonTabForFreshOpen()
fresh-open AppId captureStatus used only for top-level selection
```

The two top-level descriptor keys should also be retired:

```text
steam-input-addon-device
steam-input-addon-profile
```

Do not remove their stale-generation cleanup compatibility until the migration path has explicitly removed old injected descriptors from retained Steam tab arrays.

A single generation should end with exactly one current Addon descriptor.

---

## 16. Reinjection / cleanup requirements

PR #518 established a real live-CEF requirement:

> descriptor objects that close over an old script generation must not survive into a new generation.

The new topology must preserve this rule.

On install/reinstall/teardown:

```text
- clear cached current Addon descriptor;
- remove stale legacy Addon-owned descriptors only;
- remove stale Device/Profile descriptors from PR #521/#522 if still present;
- preserve every unrelated Steam/native descriptor;
- ensure exactly one current Addon descriptor is installed.
```

Do not identify stale Addon descriptors by display title.

Use owned markers/known historical stable keys.

---

## 17. Native UI / input acceptance goals

The inner native `Tabs` PoC should be validated on real hardware for:

- D-pad left/right focus movement where applicable;
- L1/R1 inner-tab switching if the native component exposes the same navigation behavior in the QAM context;
- correct native focus highlight;
- controller activation of Toggle/Slider controls after inner-tab navigation;
- scroll behavior and return to tab row/content;
- Device/Profile switching with no remount-related stale state;
- Profile active-AppId changes while Profile remains selected;
- no-game Profile `No active game.` state;
- QAM close/reopen;
- QamHost reinjection / GamepadUI document reload;
- Steam Stable and Beta if both are intended to remain supported by the product.

Do not assume Friends/Chat behavior guarantees identical QAM embedding behavior. The generic `Tabs` contract is strong reference evidence, but this Addon placement still requires MSI Claw hardware acceptance.

---

## 18. Failure policy

### Native inner `Tabs` discovery fails

Fail closed.

Do not:

- generate a home-grown CSS tab bar pretending to be Steam native;
- fall back to DOM selectors;
- broaden semantic matching until something is found;
- bind to Friends/Chat private stores.

Log one low-noise diagnostic identifying that native Tabs discovery did not resolve uniquely.

### Top-level fresh-open selection seam remains unresolved

The Addon tab can remain manually selectable while the PoC continues, but the product requirement:

```text
fresh QAM open -> Addon
```

is not complete.

Do not add repeated selection retries or speculative open-state machinery merely to make it appear reliable.

---

## 19. Overengineering guard

The preferred end state is intentionally small:

```text
one Addon top-level descriptor
one Steam native inner Tabs component
one local active-inner-tab value
existing Device/Profile QuickSettingsPanel implementation
existing shared frontend authority
one exact verified fresh-open top-level selection seam
```

Avoid creating:

- QamTabManager;
- QamLifecycleService;
- selection epochs;
- owner generations;
- focus managers;
- polling loops;
- separate Device/Profile product state;
- duplicate policy tables;
- additional controller/runtime authority.

If the final implementation needs substantially more state than the topology above, re-check whether the code is protecting a real supported lifecycle failure or compensating for an unverified Steam assumption.

---

## 20. Suggested implementation staging

Do not combine every unknown into one production PR.

### PoC A — Single Addon descriptor + native inner Tabs

Goal:

```text
Steam QAM
└─ Addon
     └─ [ Device | Profile ]
```

Scope:

- resolve generic Steam native `Tabs`;
- inject one Addon descriptor;
- mount current Device/Profile panel content inside native inner tabs;
- remove the two current top-level descriptors in that PoC branch;
- no new fresh-open lifecycle implementation yet;
- keep manual selection of the Addon top-level tab acceptable for this PoC only.

Acceptance proves:

- topology;
- native navigation/focus;
- current Device/Profile content integration;
- reinjection cleanup.

### PoC B — Observe exact QAM open/top-level selection path

Goal:

- bounded instrumentation around the exact verified MenuStore path;
- distinguish fresh open from manual tab navigation if possible;
- confirm what happens on OEM1 close/toggle.

No speculative production state.

### PoC B0 — Direct closed-QAM injected-key call

This can be run before or alongside PoC A using the current Device key.

Goal:

```text
closed QAM
-> OpenQuickAccessMenu("steam-input-addon-device", false)
-> determine whether Steam opens QAM directly on that injected key
```

Run both cold-first-open and warm-after-descriptor-insertion cases from section 12.1. This is the fastest way to determine whether the native operation can replace lifecycle detection entirely or whether descriptor ordering is the real constraint.

### Production PR — Verified fresh-open Addon selection + cleanup

Only after PoC evidence, choose the smallest proven path:

```text
Path 1 — direct native operation
OEM1 explicit request -> QamHost -> OpenQuickAccessMenu(AddonKey)
only if open/select/toggle/focus semantics are hardware-proven

Path 2 — existing pulse + exact native fresh-open selection seam
keep system-button pulse as open/close authority
override only the verified open-time selected key

Path 3 — explicit one-shot request + existing pulse
use only if descriptor ordering prevents direct closed-QAM custom-key open
consume selection once after the Addon descriptor exists
```

For every path:

- remove PR #522 `visible`-based lifecycle code;
- remove obsolete two-top-level-tab selection state;
- do not add polling/retry/epoch machinery.

If PoC A and B/B0 are all small and the live seam is obvious, they may be folded into one implementation branch, but review should still treat topology proof, direct-open proof, and fresh-open/toggle proof as separate acceptance questions.

---

## 21. Source references reviewed

### Project repository

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/QAM_NATIVE_02_DEVICE_PROFILE_STABLE_TABS_WORK_ORDER.md`
- `docs/work-order/QAM_NATIVE_02A_ALWAYS_OPEN_ADDON_TAB_HOTFIX_WORK_ORDER.md`
- `src/SteamInputAddonforClaw.QamHost/Frontend/qam.js`
- `src/SteamInputAddonforClaw.QamHost/Program.cs`
- `src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs`
- `src/SteamInputAddonforClaw/Lifecycle/QamHostProcessController.cs`
- `src/SteamInputAddonforClaw/CenterM/FrontButtonActionExecutor.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs`
- relevant QAM/frontend/controller tests

### External reference repositories

- `KillerPixelCrew/WSGM`, branch `2.0`
- `KillerPixelCrew/steam-ui-toolkit`, WSGM-pinned commit `13ce887fef7828ab56dd065b545b4747e6131880`
- `SteamDeckHomebrew/decky-loader`
  - `frontend/src/tabs-hook.tsx`
  - `frontend/src/components/QuickAccessVisibleState.tsx`
- `SteamDeckHomebrew/decky-frontend-lib`
  - `src/components/Tabs.ts`
  - 2026-03-22 commit `3126dd3e040eaef00eb0362b69efb143d7e01030` (`fix(Tabs): update for latest beta (#129)`)

External sources are reference-only. Do not add WSGM, Decky Loader, or `decky-frontend-lib` as project runtime dependencies merely to implement this design.

---

## 22. Supplemental decision notes — how to reconcile the alternatives

The earlier sections intentionally prefer preserving the known-good SteamDeck Quick Access pulse until direct native behavior is proven. The additional findings do **not** invalidate that conservative recommendation; they make the next decision testable instead of leaving "fresh-open lifecycle" as the only design path.

The decision order should be:

```text
1. Prove single Addon top-level + native inner Tabs.

2. Directly test OpenQuickAccessMenu(customAddonKey, false)
   on a closed QAM:
   - cold before known descriptor insertion
   - warm after known descriptor insertion
   - repeated while already open

3. If direct open/select/toggle/focus behavior is fully correct:
   -> prefer the direct native operation.
   -> no QAM-open lifecycle detection is needed.

4. If direct open is not sufficient, but an exact native fresh-open selection seam is proven:
   -> keep the SteamDeck Quick Access pulse as open/close authority.
   -> override only that proven selected-key seam once.

5. If descriptor ordering prevents direct closed-QAM open and no clean fresh-open seam exists:
   -> use one explicit OEM1-caused pending selection + the existing pulse.
   -> consume once when the Addon descriptor is present.

6. In all cases:
   -> do not return to args[0].visible lifecycle inference.
   -> do not add timers, retries, epochs, owner identities, or generic managers.
```

This keeps the original document's caution while recognizing a potentially simpler result: because the Addon already owns the OEM1 action path, the best architecture may be to make the **operation that requests QAM** target the Addon key directly rather than infer afterward that a QAM open happened.

The decisive evidence must come from the bounded hardware PoCs above, especially the cold-vs-warm descriptor-ordering test and repeat-press toggle/focus behavior.

---

## 23. Final recommendation

Adopt the single-top-level Addon topology.

```text
Steam QAM
└─ Addon
     ├─ Device
     └─ Profile
```

Use the Steam generic native `Tabs` primitive for Device/Profile and preserve the current shared frontend authority underneath it.

Treat PR #522's hardware failure as a failure of the guessed `args[0].visible` fresh-open lifecycle authority, not as a failure of Addon tab injection or proof that `MenuStore.OpenQuickAccessMenu` cannot select an injected key.

Do not patch the failed lifecycle design with more owner/session state.

Instead:

1. prove the single Addon + native inner Tabs topology on hardware;
2. run the direct closed-QAM injected-key PoC, including cold-before-insertion and warm-after-insertion cases;
3. observe the exact native QAM open/top-level selection and repeat-press toggle behavior;
4. choose the smallest proven path: direct native open/select if fully equivalent, otherwise pulse + exact selection seam, otherwise explicit one-shot request + pulse if descriptor ordering requires it;
5. reuse the existing `.Qam` transport if an explicit Runtime -> QamHost request is chosen rather than creating another IPC subsystem;
6. delete the obsolete PR #522 visibility lifecycle state once the replacement seam is hardware-proven.

This yields the smallest architecture consistent with the desired UX and the project's Full1902 / anti-overengineering policy.

---

## 24. Supplemental research — can Steam native QAM replace the Addon-owned Overlay on Windows Desktop?

> **Status:** Promising external evidence, but **not yet hardware-proven for the required no-game / no-BPM Windows Desktop state**.  
> **Relationship to sections 13/20/23:** This is an additional product/architecture question. It does not invalidate the existing conservative QAM-selection conclusions above.

A broader question emerged while investigating direct QAM opening:

> If Steam is running in normal Windows Desktop mode, with no Steam game and no Big Picture session, can the Addon directly surface Steam's native QAM and use it as the Quick Settings renderer?

If the answer is yes, the separate Addon-owned WinUI Overlay may be unnecessary for the common Steam-running product path. The existing shared Device/Profile frontend authority could remain unchanged while Steam QAM becomes the single handheld Quick Settings presentation.

This possibility should be tested before investing further in Overlay-specific presentation work, but it must not be treated as proven from API shape alone.

### 24.1 New external evidence: generic QAM opening is not structurally tied to a running Steam game

The current `toonymak1993/tools-for-steam` implementation contains two meaningfully different Steam paths.

Its generic direct QAM opener, `TryOpenQuickAccessMenuAsync`, evaluates an `OpenQuickAccessMenu` / `ShowQuickAccessMenu` style operation across Steam CEF surfaces. Candidate targets include:

```text
SharedJSContext
Big Picture / GamepadUI main target
MainMenu / Menu
QuickAccess
```

and candidate runtime objects include conceptually:

```text
window.GamepadUI
window.GamepadUI.Router
window.GamepadUI.NavigationManager
window.SteamUIStore
window.SteamUIStore.MenuStore
window.SteamUIStore.SideMenuStore
window.SteamClient.UI
window.SteamClient.Overlay
window.SteamClient.Input
window.SteamClient.System
window.SteamClient
```

Importantly, this generic direct opener does **not** first require a running Steam AppId.

The same project has a separate in-game overlay path, `TryOpenInGameOverlayAsync`, which explicitly resolves:

```text
MainRunningAppID / running app
-> overlay instance
-> gameid
-> SteamClient.Overlay.SetOverlayState(...)
-> OnQuickAccessButtonPressed()
   or overlayInstance.MenuStore.OpenQuickAccessMenu()
```

and fails when no running AppId / game id can be resolved.

That separation is useful evidence:

```text
generic Steam QAM request
!=
in-game overlay-instance QAM request
```

Therefore the native QAM request operation itself is not obviously coupled to an active Steam game.

This still does **not** prove that Windows Desktop idle Steam owns or will materialize a visible QAM surface. It only removes one earlier assumption: a running game is not necessarily required by the direct opener's API path.

### 24.2 Stronger evidence: QAM can be surfaced without an injected in-game Steam overlay renderer

`tools-for-steam` also has `ExternalGameQuickAccessService`.

Its documented purpose is to open Steam's real Big Picture Quick Access surface in front of a foreground game whose process has **no injected Steam overlay renderer**.

The flow is conceptually:

```text
foreground external game
-> verify Steam overlay renderer is missing
-> focus Steam window
-> TryOpenQuickAccessMenuAsync()
-> fallback to Ctrl+2 if needed
-> observe QuickAccess visibility
-> restore the original game window when QAM closes
```

This is stronger evidence than the direct method signature alone because it demonstrates an intended QAM path outside the normal injected in-game overlay renderer.

However, it is still not the exact product case required here:

```text
required Addon question:
Steam running
+ Windows Desktop foreground
+ no Steam game
+ no BPM
+ no existing in-game overlay
-> can QAM appear by itself?
```

`ExternalGameQuickAccessService` still requires a foreground game target, and its own description calls the presented surface the "real Big Picture Quick Access surface". It therefore may depend on a GamepadUI/Big-Picture surface that already exists or can be activated as part of that flow.

So the correct conclusion is:

```text
QAM outside injected in-game overlay = supported by external evidence
pure idle Windows Desktop QAM       = still unproven
```

### 24.3 Current Addon code cannot answer the desktop-idle question yet because QamHost is intentionally stopped there

The current project already has a product/lifetime gate before the Steam behavior can even be tested.

`QamHostProcessController` currently computes desired QamHost lifetime as:

```text
Big Picture active
OR
actual Steam game active
```

Conceptually:

```csharp
desired = _bigPictureActive || _steamGameActive;
```

Therefore:

```text
Steam running normally on Windows Desktop
+ no game
+ no BPM
-> QamHost not desired
-> current Addon never attempts the direct desktop QAM path
```

The README is consistent with that current boundary: QAM support is documented for Big Picture sessions and Desktop Steam games that receive Steam's GamepadUI in-game overlay.

This means a failed current-product desktop test would not by itself prove Steam cannot do it. The existing QamHost lifetime prevents the experiment.

For the PoC, temporarily/manual-start QamHost or use a narrow diagnostic lifetime override. Do **not** broaden the production lifetime first and then infer success from the resulting behavior.

### 24.4 Required desktop/no-game/no-BPM hardware PoC

Run this independently from the existing game/BPM QAM acceptance tests.

#### Case I — Steam Desktop idle, no game, no BPM

Preconditions:

```text
Steam client running normally
Big Picture not active
no Steam game / Non-Steam shortcut running
current controller presentation remains Xbox360
QAM closed
```

Diagnostic steps:

```text
1. Record Steam CDP targets before the request.
2. Start QamHost only for the diagnostic if the normal lifetime gate keeps it stopped.
3. Resolve SharedJSContext and any existing GamepadUI/MenuStore candidates.
4. Invoke the bounded direct Quick Access operation once.
5. Record:
   - return/result
   - targets before/after
   - whether a new QuickAccess/GamepadUI target appears
   - foreground/focus result
   - whether the full Big Picture main UI appears
6. If QAM appears, verify Addon descriptor injection and current shared Device controls.
7. Close and reopen QAM and verify native focus/toggle behavior.
```

The PoC must not temporarily switch the controller to SteamDeck merely to make the test pass. The first question is whether native QAM can be a presentation-only desktop surface while the supported Full1902 controller authority remains unchanged.

#### PASS

Treat desktop native QAM as viable only if all of the following are true:

```text
- QAM appears while Steam remains in normal Desktop mode;
- full Big Picture main UI does not take over the desktop;
- no Steam game is required;
- Addon descriptor/content can be injected after the surface materializes;
- current Xbox360 presentation can navigate/operate the QAM acceptably;
- close/reopen/focus behavior is native and predictable;
- no PID1901/PID1902 or virtual-presentation transition is required;
- Steam desktop/client state is restored cleanly after QAM closes.
```

If these conditions hold, the Addon has a credible path to use Steam native QAM as its Windows Desktop Quick Settings renderer.

#### PARTIAL — not enough to replace the Overlay

Do **not** count the PoC as successful if QAM only becomes usable after any of these:

```text
- forcing Steam into full Big Picture / Gamepad UI mode;
- showing the Big Picture main UI behind or before QAM;
- switching Xbox360 -> SteamDeck solely to obtain a system-button pulse;
- requiring a dummy/running Steam AppId;
- relying on repeated focus tricks or retries.
```

In particular, a production design such as:

```text
SetUIMode(Gamepad)
-> launch/show Big Picture
-> hide its main window somehow
-> expose only QAM
```

would replace a simple dedicated Overlay with a more fragile Steam lifecycle workaround. Do not choose that merely to avoid maintaining Overlay XAML.

#### FAIL

If the direct operation is accepted by the CEF context but no independent visible QAM can be materialized without a game/BPM surface, keep the current product split:

```text
Steam game / BPM
-> Steam native QAM

Windows Desktop / unsupported Steam surface
-> Addon-owned Overlay
```

Do not create another speculative QAM lifecycle manager to force the unsupported state.

### 24.5 Product dependency question: Steam running vs true standalone Quick Settings

Even a full Case I PASS does not automatically mean the Overlay should be deleted.

Steam native QAM has an unavoidable dependency:

```text
Steam client must be running and sufficiently healthy to provide GamepadUI/QAM.
```

The Addon-owned Overlay architecture was designed to work over:

```text
Game / Windows Desktop
```

without depending on Steam GamepadUI as its renderer.

Therefore the product must explicitly choose one of these contracts:

```text
Contract A — Steam-running Quick Settings is sufficient
-> native desktop QAM may replace the Addon Overlay

Contract B — Quick Settings must still work when Steam is not running / unavailable
-> Steam QAM cannot be the only renderer
-> keep an Addon-owned fallback surface or accept that Quick Settings is unavailable
```

Because this application is Steam Addon for Claw, Contract A may be completely reasonable, but it should be an explicit product decision rather than an accidental consequence of removing the Overlay.

Do not preserve two full-featured renderers indefinitely merely because both are technically possible. If Steam-running QAM becomes the product contract, prefer one primary renderer and delete redundant presentation code after acceptance.

### 24.6 Architecture consequence if desktop native QAM is proven

If Case I passes and the product accepts Steam-running Quick Settings, the preferred simplified architecture becomes:

```text
Windows Desktop + Steam running
BPM
Steam game
        │
        └-> QamHost / Steam native QAM
              └-> one Addon top-level tab
                    └-> native inner Device / Profile Tabs
```

The shared frontend/product authority remains unchanged.

The main architectural change is QamHost lifetime, not controller ownership.

A future implementation should consider the smallest lifetime rule, for example:

```text
on-demand QamHost start for an explicit QAM request
```

or, if justified by measured cost and simpler behavior:

```text
QamHost available while Steam is running
```

Do not decide between those until the PoC proves the desktop surface and actual startup/show latency is measured.

Critically, desktop QAM must **not** become a reason to change the virtual presentation policy:

```text
no game / no BPM
-> remain Xbox360 if that is the normal presentation policy
```

If an explicit OEM1/QAM request is needed while Xbox360 is active, prefer a narrow Runtime -> existing `.Qam` transport -> QamHost direct-open operation rather than temporarily changing to SteamDeck only to synthesize a Quick Access system-button pulse.

### 24.7 Lifecycle/failure rules if QamHost lifetime is later broadened

Any later production change must preserve the existing Full1902 boundary:

```text
QamHost crash/exit
Steam exit
Steam CEF reload
QAM close
Windows suspend/resume
```

must remain frontend failures only.

They must not:

- release PID1902 ownership;
- tear down VIIPER because a frontend vanished;
- alter HidHide authority;
- switch Xbox360/SteamDeck presentation by themselves;
- reset Device/Profile feature authority.

If Steam exits while a desktop QAM is open:

```text
QAM disappears / QamHost retires
-> Runtime continues
-> controller authority continues
-> main UI remains available independently
```

No additional controller state machine is justified for this presentation experiment.

### 24.8 Recommended staging change

Before spending additional implementation effort polishing the separate Addon Overlay as the long-term Windows Desktop Quick Settings surface, add one bounded hardware PoC:

```text
PoC C — Desktop native QAM viability

Steam running
no game
no BPM
Xbox360 presentation unchanged
-> direct native QAM request
-> can QAM appear alone and host the Addon tab?
```

Decision:

```text
PoC C PASS
+ Steam-running dependency accepted
-> revisit whether SteamInputAddonforClaw.Overlay.exe should exist at all
-> prefer QAM-only Quick Settings presentation if it materially simplifies the product

PoC C PASS
+ Steam-off Quick Settings still required
-> decide explicitly whether a small fallback renderer is worth keeping

PoC C PARTIAL/FAIL
-> keep the Addon Overlay for the desktop/non-QAM product domain
```

Do not delete the current Overlay implementation/design solely from external source evidence. The deletion decision should follow real MSI Claw hardware proof of Case I plus the product decision about Steam availability.

### 24.9 Additional references reviewed for this supplement

Project repository:

- `src/SteamInputAddonforClaw/Lifecycle/QamHostProcessController.cs`
- `README.md` — current QAM support boundary
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`

External reference repository:

- `toonymak1993/tools-for-steam`
  - `src/SteamLoader.App/Infrastructure/Steam/SteamDevToolsClient.cs`
  - `src/SteamLoader.App/Services/ExternalGameQuickAccessService.cs`
  - `src/SteamLoader.App/Hosting/SteamLoaderBackgroundHost.cs`

The external implementation is reference evidence only. Do not add it as a runtime dependency.

---

## 25. Updated decision checkpoint

The document's original recommendation remains valid for the current supported game/BPM QAM domain:

```text
one Addon top-level QAM tab
+ native inner Device/Profile Tabs
+ smallest proven direct/open-selection seam
```

The new desktop research adds one checkpoint **before finalizing the separate Overlay as a permanent renderer**:

```text
Can Steam native QAM exist as a clean no-game / no-BPM Desktop surface?
```

If hardware says yes, the simplest product may become:

```text
one Quick Settings renderer = Steam native QAM
```

rather than maintaining:

```text
Steam QAM renderer
+
Addon WinUI Overlay renderer
```

If hardware says no, the existing split remains justified.

This question should be answered by the bounded PoC C, not by expanding Steam lifecycle state, forcing Big Picture, or adding another abstraction layer.