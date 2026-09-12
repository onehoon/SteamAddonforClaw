# Work Order — QAM-NATIVE-02: Stable Device / Profile Tabs

> **Date:** 2026-09-12  
> **Status:** Ready for implementation  
> **Reviewed baseline:** `main` at `e6beadd8d8151eb2247d74968584419768c28aa6`  
> **Baseline includes:** QAM-NATIVE-01 / PR #515, stale descriptor cleanup / PR #518, and current `main` through PR #519  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and the documents it references  
> **Scope:** Replace the current single dynamic Addon QAM tab with two stable Addon-owned tabs, `Device` and `Profile`, while preserving the shared Quick Settings model, Steam-native controls, mutation safety, and Full1902 ownership boundaries.

---

## 1. Goal

QAM-NATIVE-01 established a working Steam-native Quick Settings surface while deliberately retaining the old one-tab topology:

```text
no active game -> Addon tab renders Device page
active game    -> same Addon tab renders Profile page
```

QAM-NATIVE-02 removes that dynamic surface identity.

The target is:

```text
Steam QAM
  ├─ native Steam tabs...
  ├─ Device   <- stable Addon-owned tab
  └─ Profile  <- stable Addon-owned tab
```

Both Addon tabs must exist for the entire live QAM session.

The tab that opens by default depends on the current Steam game context only when a fresh QAM surface is opened:

```text
no active game -> Device
active game    -> Profile
```

After the QAM is already open, later game start/exit/AppId changes must update Profile content without stealing the user's current tab selection.

This PR is a topology/view-binding change. It is **not** a new Runtime architecture and must not create another owner for controller lifecycle, profile state, feature policy, or persistence.

---

## 2. Required documents and source to read first

Read these before editing:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/QAM_NATIVE_01_STEAM_NATIVE_SURFACE_AND_INTERACTION_RECOVERY_WORK_ORDER.md

docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
docs/shared-frontend/SF_V2_08_SHARED_PROFILE_PROJECTION_DISPATCH_QAM_GENERIC_MIGRATION_WORK_ORDER.md
docs/shared-frontend/SF_V2_09_OVERLAY_PROFILE_PUBLICATION_GENERIC_BINDING_WORK_ORDER.md

src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsMutationAdapterTests.cs
```

External reference remains reference-only:

```text
KillerPixelCrew/WSGM branch 2.0
KillerPixelCrew/steam-ui-toolkit
pinned toolkit commit: 13ce887fef7828ab56dd065b545b4747e6131880
```

Do not add WSGM or `steam-ui-toolkit` as a dependency.

---

## 3. Current baseline verified on `main`

### 3.1 QAM-NATIVE-01 is already the native-control authority

Do not redo the PR1 work.

Current `qam.js` already has:

- deterministic semantic native component discovery;
- native `PanelSection` / `PanelSectionRow`;
- controlled native `ToggleField`;
- native `SliderField` value/suffix/bookend/notch presentation;
- generic Device/Profile page rendering from shared snapshots;
- generic mutation dispatch;
- low-noise page and mutation diagnostics;
- stale Profile AppId protection;
- generic pending slider/TDP scheduling.

QAM-NATIVE-02 must preserve those behaviors rather than replacing them with another QAM renderer.

### 3.2 PR #518 stale descriptor cleanup is part of the required baseline

The Addon QAM global survives inside Steam's CEF environment across script reinjection. PR #518 fixed the concrete stale-generation problem by clearing `state.addonTabDescriptor` on new installation generation and teardown.

The two-tab implementation must preserve the same rule:

> An Addon tab descriptor must never survive into a new script generation while closing over React/native components or panel implementations from an older generation.

If descriptor storage changes from one descriptor to two descriptors, the install/uninstall cleanup contract must change with it and retain equivalent regression coverage.

### 3.3 Current panel still owns dynamic Device/Profile selection

Current refresh logic still does conceptually:

```js
const nextStatus = await request("captureStatus");
const nextAppId = Number(nextStatus?.steam?.appId || 0);
const activeGame = nextAppId > 0;
const nextContext = activeGame
  ? { pageId: QS_PAGE_PROFILE, appId: nextAppId }
  : { pageId: QS_PAGE_DEVICE, appId: null };
```

That decision belongs to the old one-tab topology and must disappear.

After this PR, each mounted panel must have a fixed page identity.

### 3.4 Current invalidation subscription is single-consumer

Current bridge notification path uses one global callback:

```js
state.onStateInvalidated = refresh;
```

and:

```js
if (kind === "state-invalidated") state.onStateInvalidated?.();
```

That is sufficient for one mounted Addon panel but not for two independent mounted views.

This is a real topology requirement. Replace it with the smallest possible multi-subscriber primitive. Do **not** introduce an event bus, manager, service, dispatcher abstraction, epoch model, or new state machine.

### 3.5 Shared frontend remains product authority

JavaScript must continue to treat the shared page payload as authoritative for:

- page availability;
- row availability/writability;
- section/row order;
- labels;
- slider ranges/steps/suffixes;
- discrete options;
- linked constraints;
- commit grouping;
- failure messages.

QAM must not recreate Device/Profile product rules locally.

---

## 4. Frozen product behavior

### 4.1 Stable Addon tab identities

Use stable keys:

```js
const ADDON_DEVICE_TAB_KEY = "steam-input-addon-device";
const ADDON_PROFILE_TAB_KEY = "steam-input-addon-profile";
```

There must be exactly one Addon Device tab and exactly one Addon Profile tab.

The order is fixed:

```text
Device
Profile
```

Do not keep the old single `steam-input-addon` tab as a third tab.

### 4.2 Context matrix

| Runtime context | Device tab | Profile tab | fresh-open default |
| --- | --- | --- | --- |
| BPM / no game | visible + writable according to shared Device page | visible + selectable, shared unavailable/no-game state | Device |
| Steam game running | visible + writable according to shared Device page | visible + current active AppId Profile | Profile |
| bridge/runtime unavailable | visible but fail-closed content | visible but fail-closed content | do not force an invalid selection |

### 4.3 Device remains usable while a game runs

Do **not** reintroduce any QAM-only rule equivalent to:

```text
Steam must be Big Picture
AND
AppId must be 0
```

for Device mutation.

QAM-NATIVE-01 removed that duplicated admission intentionally.

Runtime feature owners already resolve active-game precedence for Device edits. For example, Device TDP/CPU/Power settings may be persisted while an active game Profile remains the effective applied value.

The row mutation gate remains conceptually:

```js
row.available === true &&
row.writable === true &&
!busy
```

### 4.4 Profile target validation stays strict

Do not weaken existing Profile safety.

Required invariant remains:

```text
Profile mutation AppId > 0
AND intent AppId == current active Profile target
AND fresh projected edited row is still Available + Writable
```

The central mutation adapter remains the final authority.

### 4.5 No-game Profile state uses shared authority

When there is no active game, the Profile tab must remain visible/selectable.

It must show the shared unavailable Profile state, with the existing product message:

```text
No active game.
```

Do not synthesize fake Profile controls in JavaScript merely to display disabled rows.

If the current generic QAM capture seam for `(Profile, null)` does not expose the existing explicit no-game message, make only the narrow shared-frontend adjustment required to return the authoritative unavailable Profile snapshot.

Example acceptable shape:

```csharp
(QuickSettingsPageId.Profile, null)
    => QuickSettingsPageSnapshot.Unavailable(
        QuickSettingsPageId.Profile,
        null,
        "No active game.");
```

Use the existing shared presentation/host convention where possible. Do not create a QAM-specific copy of this policy.

---

## 5. Required implementation

### 5.1 Replace one dynamic descriptor with two stable descriptors

Current code caches one `state.addonTabDescriptor` and inserts one marked tab.

Refactor this into two stable descriptors for the current script generation.

Suggested local representation:

```js
state.addonTabDescriptors = {
  device: ...,
  profile: ...,
};
```

or another equally small representation.

Do not create a `TabManager` class or similar abstraction.

Each descriptor must have its own stable key and fixed panel page identity.

Conceptually:

```js
buildAddonTab(React, native, {
  key: ADDON_DEVICE_TAB_KEY,
  pageId: QS_PAGE_DEVICE,
});

buildAddonTab(React, native, {
  key: ADDON_PROFILE_TAB_KEY,
  pageId: QS_PAGE_PROFILE,
});
```

### 5.2 Upgrade Addon-owned tab marking and deduplication

The current legacy descriptor uses:

```js
[TAB_MARKER]: true
key: "steam-input-addon"
```

The new topology needs to distinguish Device and Profile.

Prefer using the existing Addon marker with the stable Addon tab key as its value, for example:

```js
[TAB_MARKER]: ADDON_DEVICE_TAB_KEY
[TAB_MARKER]: ADDON_PROFILE_TAB_KEY
```

During reinjection, treat the old boolean marker as a legacy Addon descriptor owned by this Addon.

The insertion path must:

1. identify only Addon-owned descriptors;
2. remove any stale legacy one-tab descriptor;
3. remove duplicate Addon Device/Profile descriptors if present;
4. preserve all Steam/native/non-Addon tabs untouched;
5. insert exactly one Device followed by one Profile descriptor.

A simple helper is sufficient. Example shape:

```js
function addonTabKey(tab) {
  const marker = tab?.[TAB_MARKER];
  if (marker === true) return "legacy";
  if (marker === ADDON_DEVICE_TAB_KEY) return ADDON_DEVICE_TAB_KEY;
  if (marker === ADDON_PROFILE_TAB_KEY) return ADDON_PROFILE_TAB_KEY;
  return null;
}
```

Do not identify Addon tabs by visible text/title, DOM structure, or numeric array position.

### 5.3 Preserve PR #518 generation cleanup

At the beginning of a new install generation, clear the complete descriptor cache.

On uninstall, clear the complete descriptor cache even on the existing `!state.installed` early-return path.

Teardown must remove both live Addon descriptors from any retained `tabs` arrays exactly as the current single-tab cleanup removes the one Addon descriptor.

Do not broaden cleanup to unrelated Steam tabs.

### 5.4 Make panel page identity explicit

Refactor the current dynamic panel into an explicit fixed-page component.

Suggested shape:

```js
function QuickSettingsPanel({ pageId }) {
  ...
}
```

or equivalent.

The component must not decide whether it is Device or Profile based on AppId.

#### Device panel refresh

Device context is always:

```text
PageId = Device
AppId = null
```

Request the shared Device page directly.

Do not call `captureStatus` solely to decide whether Device should become Profile.

#### Profile panel refresh

Profile must resolve the current active Steam AppId.

Conceptually:

```js
const status = await request("captureStatus");
const appId = Number(status?.steam?.appId || 0);

const context = {
  pageId: QS_PAGE_PROFILE,
  appId: appId > 0 ? appId : null,
};
```

Then capture the Profile page for that context.

When the active AppId changes:

```text
old game -> new game
old game -> no game
no game  -> new game
```

retire only pending work belonging to the old Profile context.

A Profile context transition must **not** cancel Device pending work.

### 5.5 Keep PageId + AppId as the mutation/draft identity

Do not redesign the generic scheduler.

Preserve:

- `quickSettingsPendingKey(...)` identity;
- `cancelQuickSettingsPendingForContext(...)`;
- whole-section TDP commit grouping;
- linked slider constraints;
- trailing commit policy;
- fresh-page pruning;
- Profile preflight before delayed RPC;
- stale late-result guard;
- mutation-depth invalidation deferral.

Required behavior after the two-tab split:

```text
Device pending slider
+ game starts/exits/changes
=> Device pending slider remains valid unless Device's own fresh page invalidates it.
```

```text
Profile(AppId=A) pending slider
+ active target becomes B or none
=> AppId=A pending work is retired and cannot mutate/settle B.
```

### 5.6 Replace the single invalidation callback with a minimal subscriber set

Use one small Addon-owned `Set`.

Suggested shape:

```js
state.stateInvalidationSubscribers ??= new Set();

function subscribeStateInvalidation(callback) {
  state.stateInvalidationSubscribers.add(callback);
  return () => state.stateInvalidationSubscribers?.delete(callback);
}
```

Bridge notification:

```js
function receiveBridgeNotification(kind) {
  if (kind !== "state-invalidated") return;

  for (const callback of [...(state.stateInvalidationSubscribers ?? [])]) {
    try {
      callback();
    } catch (error) {
      logOnce(
        "stateInvalidationSubscriberFailure",
        `QAM state invalidation subscriber failed: ${String(error)}`
      );
    }
  }
}
```

Each mounted Device/Profile panel registers its own refresh callback and unregisters it in React cleanup.

`retireBridgeConsumers()` must clear the subscriber set after retiring pending bridge work.

Do not add:

- an event bus;
- observable framework;
- manager/service class;
- sequence number/epoch just for invalidation;
- locks/barriers;
- per-tab background polling.

The existing refresh-in-flight/dirty mechanism remains sufficient for each panel.

---

## 6. Fresh-open default tab selection

### 6.1 Required user behavior

When a fresh QAM is opened:

```text
active AppId == 0 -> Device selected initially
active AppId > 0  -> Profile selected initially
```

This decision is a **fresh-open default only**.

Once the QAM is open, later AppId changes must not force a different selected Addon tab.

Examples:

```text
User is viewing Device
-> launches a game while QAM remains open
-> Device stays selected
-> Profile silently refreshes to that game
```

```text
User is viewing Profile
-> game exits while QAM remains open
-> Profile stays selected
-> content becomes shared "No active game." unavailable state
```

### 6.2 Use Steam's existing native tab-selection authority only

Do not implement default selection by manipulating rendered DOM.

Forbidden:

```text
document.querySelector(...)
element.click()
focus() tricks
synthetic keyboard input
synthetic controller input
setInterval polling
repeated setTimeout retries
MutationObserver used to click/select the tab
```

Also do not invent a `MenuStore` API based on assumptions. The reviewed `steam-ui-toolkit` reference does not provide a verified generic QAM selected-tab API that can simply be copied.

During implementation, resolve the selected-tab authority from the current Steam QAM tab owner/native component path already being patched. Use it only when the selection seam is deterministic and uniquely identified in the supported current Steam build.

The default-selection write must happen once for each fresh QAM open/owner instance, after both Addon descriptors exist.

Do not re-run the default-selection decision on ordinary state invalidation or AppId changes.

### 6.3 Fail open for selection only

If Steam's native selected-tab authority cannot be resolved unambiguously on a future Steam build:

- keep both Addon tabs inserted;
- keep both tabs usable/selectable by the user;
- do not disable the Addon QAM surface;
- do not guess a property or synthesize input;
- emit one low-noise diagnostic;
- leave Steam's current selection unchanged.

This fallback applies only to initial default selection. It must not weaken page/mutation fail-closed behavior.

---

## 7. Bridge and shared frontend scope

### 7.1 Do not add a second bridge protocol

The existing generic methods are sufficient:

```text
captureStatus
captureQuickSettingsPage
mutateQuickSettings
```

Do not add methods such as:

```text
captureDevicePage
captureProfilePage
selectDeviceTab
selectProfileTab
```

unless a concrete current source limitation proves the generic seam cannot express the required operation. No such limitation is present in the reviewed baseline.

### 7.2 Narrow shared change allowed for Profile/no-game projection

If `(Profile, null)` currently loses the existing explicit no-game message, update the shared capture seam rather than synthesizing the message in `qam.js`.

The result must remain a normal `QuickSettingsPageSnapshot` unavailable page.

Do not change profile persistence, game observation, profile enable semantics, or feature apply logic.

### 7.3 Mutation adapter remains unchanged unless a concrete regression requires otherwise

The two-tab topology should not require a new mutation model.

`QuickSettingsMutationAdapter` remains authoritative for intent validation and stale Profile target rejection.

Do not relax its validation just to make QAM easier to render.

---

## 8. Diagnostics

Keep diagnostics low-noise and transition-oriented.

Recommended new/updated lines:

```text
QAM stable Addon tabs ensured. Device=true Profile=true LegacyRemoved=<n> DuplicatesRemoved=<n>
QAM initial Addon tab selection: Device Reason=NoActiveGame
QAM initial Addon tab selection: Profile AppId=<id>
QAM initial Addon tab selection unavailable; tabs remain usable.
```

Existing page-state logs should naturally distinguish the two views:

```text
QAM page state: Page=Device AppId=none ...
QAM page state: Page=Profile AppId=<id> ...
```

or Profile no-game unavailable state.

Do not log every render, every focus move, or every repeated invalidation.

---

## 9. Required tests

### 9.1 `QamFrontendContractTests`

Update/add contract coverage proving:

1. stable keys exist:

```text
steam-input-addon-device
steam-input-addon-profile
```

2. exactly two Addon descriptors are constructed for one generation;
3. Device descriptor binds fixed Device page identity;
4. Profile descriptor binds fixed Profile page identity;
5. the old dynamic `activeGame ? Profile : Device` panel identity selection is removed;
6. legacy `[TAB_MARKER]: true` one-tab descriptors are recognized as Addon-owned legacy state and retired;
7. Device/Profile dedupe does not remove non-Addon Steam tabs;
8. descriptor cache is cleared at install/uninstall, preserving PR #518's stale-generation guarantee;
9. two mounted panels use a subscriber `Set`, not one `state.onStateInvalidated` slot;
10. subscriber cleanup occurs on unmount/bridge teardown;
11. DOM click/focus/timer polling hacks are absent from initial tab selection;
12. QAM-NATIVE-01 semantic native discovery and native control props remain present;
13. generic pending scheduler/PageId+AppId safeguards remain present.

Do not write brittle tests for unrelated whitespace or local helper names when a behavioral/source-contract assertion is available.

### 9.2 Bridge/shared frontend tests

Cover as applicable:

```text
Device capture      -> (Device, null)
Profile active game -> (Profile, active AppId)
Profile no game     -> unavailable Profile page with "No active game."
```

If no C# bridge code changes are necessary, do not churn bridge tests merely to increase test count.

### 9.3 Mutation regression tests

Existing adapter tests must continue proving:

- Profile AppId must be valid;
- stale Profile AppId is rejected;
- edited Profile row must remain available/writable;
- Device intent remains independent from active Profile target identity.

Add only the minimum extra coverage needed if implementation changes shared capture behavior.

### 9.4 Build/test commands

At minimum:

```text
node --check src/SteamInputAddonforClaw.QamHost/Frontend/qam.js

dotnet build SteamInputAddonforClaw.slnx --configuration Debug --no-restore
dotnet build SteamInputAddonforClaw.slnx --configuration Release --no-restore

dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj \
  --configuration Release \
  --no-build

git diff --check
```

Run focused QAM/shared-frontend tests during development, then the full Release suite before PR completion.

---

## 10. Real-device acceptance is required

Automated tests are not sufficient for this PR because tab insertion/default selection is coupled to Steam's live GamepadUI surface.

Test on a supported MSI Claw with current Steam GamepadUI.

### 10.1 BPM / no active game

Verify:

```text
exactly one Device tab
exactly one Profile tab
no legacy third Addon tab
fresh QAM open selects Device
Device controls receive controller/keyboard focus
Device toggle mutation works
Device numeric/discrete slider mutation works
Profile tab is manually selectable
Profile shows shared no-game unavailable state
```

Expected diagnostic evidence includes Device page state and successful Device mutation request/result.

### 10.2 Game running

Verify:

```text
fresh QAM open selects Profile
Profile points at the actual current AppId
Profile controls focus and mutate
Device tab remains visible
Device tab remains usable
```

Device visibility/writability must not depend on `AppId == 0`.

### 10.3 Context transition while QAM remains open

#### Device selected, then game starts

Expected:

```text
Device remains selected
Profile refreshes to the new AppId
Device pending work is not canceled merely because Profile target changed
```

#### Profile selected, then game exits

Expected:

```text
Profile remains selected
Profile becomes shared "No active game." unavailable state
no forced jump to Device
old Profile pending work is retired
```

#### Game A -> Game B

Expected:

```text
Profile target changes A -> B
AppId A pending delayed mutation cannot execute/settle against B
no duplicate tabs
no forced tab selection change
```

### 10.4 Reinjection / teardown

Verify:

```text
close/reopen QAM
QamHost reinjection if applicable
Steam UI reload/reconnect if applicable
```

After reinjection there must still be exactly:

```text
1 Device tab
1 Profile tab
```

No descriptor from an old script generation may be reused.

No QAM lifecycle action may affect Full1902 controller ownership or routing.

---

## 11. Lifecycle and failure policy

This PR is frontend topology only.

### Must remain unaffected

```text
Sleep / Hibernate / Resume controller lifecycle
Restart / Crash / Shutdown controller cleanup
physical device loss / PnP re-enumeration
routing rollback / fail-close
HidHide ownership/recovery
VIIPER ownership/teardown
PID1901 <-> PID1902 restoration
Xbox360 / SteamDeck presentation ownership
```

QAM close, reload, injection failure, or tab-selection fallback must never become an authority for any of those domains.

### Practical failure handling

- bridge unavailable -> page fails closed;
- Profile AppId stale -> existing Runtime/shared validation rejects it;
- native tab selection seam unavailable -> both tabs remain usable and no forced selection occurs;
- Addon tab reinjection -> remove/dedupe Addon-owned descriptors only;
- component discovery failure -> preserve existing QAM-NATIVE-01 fail-closed behavior.

Do not add synchronization or state machinery for pathological timing-only interleavings that do not occur in the supported handheld lifecycle.

---

## 12. Explicit non-goals

Do **not** include any of the following in QAM-NATIVE-02:

```text
Full1902 routing/controller lifecycle changes
HidHide changes
VIIPER changes
PID1901/PID1902 changes
controller presentation changes
Overlay redesign
new performance settings
profile persistence redesign
fan control
battery limit
LED/vibration work
OEM1/WING mapping changes
new QAM native component discovery rewrite
new SliderField/ToggleField styling rewrite
DOM-driven QAM automation
new JavaScript event bus
new C# manager/service
background polling for QAM state
multi-user / RDP / Fast User Switching support
CTW integration
```

If implementation discovers an unrelated defect, report it separately rather than expanding this PR.

---

## 13. Suggested implementation sequence

Keep the implementation reviewable in this order:

1. introduce stable Device/Profile tab keys and Addon marker identity;
2. convert descriptor cache from one descriptor to two generation-scoped descriptors;
3. update insertion/dedupe/legacy cleanup and teardown;
4. refactor `QuickSettingsPanel` to explicit fixed `pageId`;
5. split Device refresh from Profile active-AppId resolution;
6. replace the single invalidation callback with a small subscriber `Set`;
7. expose the existing shared Profile/no-game unavailable snapshot if the generic seam needs the narrow adjustment;
8. add current-Steam native initial-selection binding without DOM/input hacks;
9. update focused tests;
10. run full test suite;
11. perform real Claw acceptance.

Do not combine this with unrelated UI polish.

---

## 14. Definition of done

QAM-NATIVE-02 is complete only when all of the following are true:

- [ ] `main` baseline behavior from QAM-NATIVE-01 remains intact.
- [ ] Exactly one Device and one Profile Addon tab are present.
- [ ] Old single Addon tab is removed/upgraded cleanly.
- [ ] Device panel identity is permanently Device.
- [ ] Profile panel identity is permanently Profile with current active AppId or shared no-game unavailable state.
- [ ] No game -> fresh-open default is Device.
- [ ] Game running -> fresh-open default is Profile.
- [ ] AppId changes while QAM is open do not steal the current tab selection.
- [ ] Device remains usable while a game runs.
- [ ] Profile stale-target safeguards remain intact.
- [ ] Device pending mutations are not canceled by Profile-only context changes.
- [ ] Old Profile pending mutations cannot cross into a new AppId.
- [ ] Two mounted panels receive shared state invalidation independently.
- [ ] PR #518 descriptor-generation cleanup remains effective for both descriptors.
- [ ] No DOM click/focus/polling workaround is used for tab selection.
- [ ] Full1902 controller lifecycle/ownership code is untouched.
- [ ] Focused QAM/shared tests pass.
- [ ] Full Release test suite passes.
- [ ] Real MSI Claw + current Steam GamepadUI acceptance passes.

Once these criteria are met, the Device/Profile QAM topology is considered complete. Any later work should be isolated to presentation polish or new product features rather than another topology/state-owner rewrite.
