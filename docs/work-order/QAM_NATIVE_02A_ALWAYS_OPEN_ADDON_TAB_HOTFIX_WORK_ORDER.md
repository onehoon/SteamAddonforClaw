# Work Order — QAM-NATIVE-02A: Always Open the Addon Device / Profile Tab

> **Date:** 2026-09-12  
> **Status:** Ready for implementation  
> **Reviewed baseline:** `main` at `06d2c979e093a35f9d9192426d6534b6c8a30642`  
> **Baseline includes:** QAM-NATIVE-01 / PR #515, stale descriptor cleanup / PR #518, stable Device/Profile tabs / PR #521  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and the documents it references  
> **Scope:** Fix only the QAM fresh-open tab-selection lifecycle so every new QAM open lands on the Addon `Device` or `Profile` tab instead of Steam's remembered native tab.

---

## 1. Goal

PR #521 successfully established two stable Addon-owned QAM tabs:

```text
Device
Profile
```

The remaining real-device failure is the **initial selection policy**.

Observed on the MSI Claw after PR #521:

```text
1. The Addon Device/Profile tabs are present and usable.
2. Open QAM while a Steam-native tab was the previously selected tab
   -> Steam opens that native tab again.
3. Select an Addon tab, close QAM, reopen
   -> Steam opens the Addon tab because it was the last selected tab.
4. Select a Steam-native tab, close QAM, reopen
   -> Steam opens that Steam-native tab again.
```

Therefore tab insertion and Addon page content are not the failing part.

The actual product requirement is now explicit:

```text
Every fresh QAM open
    active Steam AppId == 0  -> Addon Device tab
    active Steam AppId > 0   -> Addon Profile tab
```

Steam's remembered last-selected tab must **not** determine the initial view for a newly opened QAM.

However, after QAM is already open:

```text
User manually selects a Steam-native tab
-> keep that selection
-> do not repeatedly steal focus back to the Addon

QAM closes
-> selection policy becomes armed again

QAM opens again
-> select Addon Device/Profile once
```

This is a small QAM lifecycle hotfix. Do not redesign the PR #521 topology, shared Quick Settings model, controller runtime, or Full1902 architecture.

---

## 2. Required documents and source to read first

Read these before editing:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/work-order/QAM_NATIVE_01_STEAM_NATIVE_SURFACE_AND_INTERACTION_RECOVERY_WORK_ORDER.md
docs/work-order/QAM_NATIVE_02_DEVICE_PROFILE_STABLE_TABS_WORK_ORDER.md

docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md

src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
```

External references such as WSGM / `steam-ui-toolkit` remain reference-only. Do not add them as dependencies.

---

## 3. Current PR #521 behavior and the concrete bug

Current `qam.js` uses stable Addon keys:

```js
const ADDON_DEVICE_TAB_KEY = "steam-input-addon-device";
const ADDON_PROFILE_TAB_KEY = "steam-input-addon-profile";
```

That is correct and must remain unchanged.

Current initial selection is conceptually:

```js
const sessionOwner = owner?._owner;

state.initialTabSelectionOwners ??= new WeakSet();
if (state.initialTabSelectionOwners.has(sessionOwner)) return;
state.initialTabSelectionOwners.add(sessionOwner);

const authority = resolveNativeTabSelection(owner);
if (!authority) return;

void request("captureStatus").then(status => {
  const appId = Number(status?.steam?.appId || 0);
  const key = appId > 0
    ? ADDON_PROFILE_TAB_KEY
    : ADDON_DEVICE_TAB_KEY;

  authority.set(key);
});
```

This assumes:

```text
one React owner instance == one fresh QAM open
```

That assumption is not valid for the live Windows Steam GamepadUI lifecycle.

The observed behavior strongly indicates that Steam keeps the relevant QAM React surface alive while the panel is hidden/closed and later reuses it. Consequently, a `WeakSet` keyed by the owner fiber cannot represent **QAM open events**.

The selection policy is therefore consumed at most once for a long-lived owner, while Steam continues restoring its own remembered last tab on later QAM opens.

This is the real lifecycle defect to fix.

Do not respond by adding more owner identities, epochs, synthetic sessions, or generic visibility managers.

---

## 4. Frozen product policy

### 4.1 Every fresh QAM open starts in an Addon tab

The policy is mandatory:

```text
fresh QAM open + no active game
-> Device

fresh QAM open + active Steam game
-> Profile
```

This must happen regardless of which Steam-native tab was selected before the previous QAM close.

### 4.2 User choice is respected while QAM remains open

After the single initial selection for that open:

```text
Device -> user selects Steam tab
-> stay on Steam tab

Profile -> user selects Steam tab
-> stay on Steam tab
```

Do not reselect Addon tabs on:

```text
ordinary React rerender
state-invalidated bridge notification
slider/toggle mutation
Device page refresh
Profile page refresh
active AppId change while QAM remains open
Steam native tab selection by the user
```

### 4.3 Reopening re-applies the policy

After a real QAM close followed by a new open:

```text
selection policy runs once again
```

The previous manual Steam-tab selection does not carry forward as the first visible page.

### 4.4 Device/Profile choice remains AppId-driven

Do not simplify both cases to always Device.

Required:

```js
const targetKey = appId > 0
  ? ADDON_PROFILE_TAB_KEY
  : ADDON_DEVICE_TAB_KEY;
```

The two stable tabs from PR #521 remain the topology authority.

---

## 5. Required implementation approach

## 5.1 Stop using React owner lifetime as QAM-open lifetime

Remove the once-per-owner behavior from the selection policy.

The following concept must no longer be the thing that decides whether initial selection has already happened for the current open:

```js
state.initialTabSelectionOwners = new WeakSet();
state.initialTabSelectionOwners.has(owner._owner)
```

The owner may still be useful for resolving native selection state if that is proven by the live Steam shape, but it must not be the **open/close identity authority**.

Do not replace it with:

```text
WeakMap owner -> generation
owner -> timestamp
owner -> epoch
owner -> synthetic session object
```

That would preserve the wrong lifecycle assumption with more complexity.

## 5.2 Bind selection to the actual native QAM active/inactive lifecycle

`patchTabsProducer()` already discovers the QAM producer through a real lifecycle-bearing React node:

```js
candidate.props?.onFocusNavDeactivated != null
```

Use the **actual current-Steam QAM activation/deactivation seam** exposed by that live producer/component path.

Before finalizing the implementation, inspect the current live producer props and determine the exact lifecycle callback/property that represents:

```text
QAM becomes active/open
QAM becomes inactive/closed
```

Do not invent prop names from memory.

The implementation must preserve the original Steam callback and its arguments.

Conceptual shape only:

```js
function wrapQamLifecycle(node, onOpened, onClosed) {
  const originalOpen = /* exact live-verified Steam callback */;
  const originalClose = /* exact live-verified Steam callback */;

  // Preserve Steam behavior first/appropriately according to the verified callback contract.
  // Addon callback must not replace Steam's callback.
}
```

The exact property names must come from live inspection of the supported current Steam build.

### Important

If the only currently proven lifecycle property is `onFocusNavDeactivated`, do **not** guess that an `onFocusNavActivated` twin exists.

Instrument the live object first and use the exact proven counterpart or visibility/activation seam.

## 5.3 Keep the lifecycle state minimal

The desired state is conceptually no more than:

```js
state.qamSurfaceActive = false;
```

or an equally small pair of booleans if needed to distinguish "open but selection already consumed".

Example policy:

```text
inactive -> active
    mark active
    run Addon initial selection once

active -> active
    no selection

active -> inactive
    mark inactive

inactive -> inactive
    no-op
```

Do not add:

```text
epoch counters
sequence managers
state machine classes
locks
barriers
pollers
separate lifecycle service
new bridge protocol
```

A simple boolean transition is sufficient for the supported single-user/single-interactive-session product.

## 5.4 Run selection after Steam's own last-tab restore point

The real-device symptom shows that Steam remembers and restores its previous tab.

The Addon selection must execute from a native lifecycle seam whose ordering is verified to occur **after Steam has established/restored the tab state for that fresh QAM open**, or otherwise from the exact native selection path where the final open-time selection can be safely overridden once.

Do not solve ordering with arbitrary timing.

Forbidden:

```js
setTimeout(...)
setInterval(...)
requestAnimationFrame retry loops
Promise delay loops
MutationObserver selection
DOM click()
focus()
synthetic keyboard/controller input
```

Also do not call `authority.set()` from every render until it "sticks".

The fix must use one deterministic native lifecycle/selection seam.

## 5.5 Verify the actual selected-tab authority instead of trusting the PR #521 assumption

PR #521 currently assumes this exact shape:

```js
props.selectedTabKey
props.onTabSelected
```

That may still be correct, but the hotfix must **verify it on the live current Steam owner** instead of treating the existing comment as proof.

During implementation, inspect only the bounded QAM tab-owner object already found by the existing patch path.

Useful temporary diagnostics may report only candidate property names/types relevant to:

```text
tab
selected
active
focus
nav
activate/deactivate
```

Do not dump arbitrary Steam state or large object graphs.

After verification, keep only the exact supported contract and remove broad diagnostic dumping.

Acceptable final resolver shape if verified:

```js
function resolveNativeTabSelection(owner) {
  const props = owner?.props;
  if (!props) return null;

  // These exact names/types must be live-verified on the supported Steam build.
  if (typeof props.selectedTabKey !== "string" ||
      typeof props.onTabSelected !== "function") {
    return null;
  }

  return {
    current: () => props.selectedTabKey,
    set: key => props.onTabSelected(key),
  };
}
```

If the real Steam contract is different, implement the real contract instead.

Do not restore the old broad candidate-name guessing list.

## 5.6 One selection attempt per actual QAM open

When a true open transition is observed:

```js
async function selectAddonTabForFreshOpen(owner, descriptors) {
  const authority = resolveNativeTabSelection(owner);
  if (!authority) {
    logOnce(/* low-noise unavailable diagnostic */);
    return;
  }

  const status = await request("captureStatus");
  if (!state.installed || !state.qamSurfaceActive) return;

  const appId = Number(status?.steam?.appId || 0);
  const key = appId > 0
    ? ADDON_PROFILE_TAB_KEY
    : ADDON_DEVICE_TAB_KEY;

  if (!descriptors[key]) return;
  authority.set(key);
}
```

This example is illustrative, not an instruction to add another abstraction if the live seam can do it more directly.

The important invariants are:

```text
one fresh open -> at most one Addon selection write
close -> re-arm naturally through native lifecycle
reopen -> one new selection write
```

If `captureStatus` completes after the QAM has already closed, drop the result.

No additional retry is required.

## 5.7 Do not steal selection after the initial write

Once the Addon has performed the one open-time selection:

```text
user moves Device -> Steam tab
user moves Profile -> Steam tab
```

must remain untouched until the QAM closes and reopens.

Do not subscribe selected-tab changes and "correct" them.

This is not a permanently pinned tab.

It is only a **fresh-open default override**.

---

## 6. Diagnostics

Keep diagnostics low-noise and useful for real-device acceptance.

Recommended final diagnostics:

```text
QAM surface activated.
QAM open selection: Device Reason=NoActiveGame
QAM open selection: Profile AppId=<id>
QAM open selection unavailable; native selection seam not resolved.
QAM surface deactivated.
```

If lifecycle seam discovery needs one temporary diagnostic build, log a bounded summary such as:

```text
QAM lifecycle candidate props: <name>:<type>, ...
QAM tab selection candidate props: <name>:<type>, ...
```

Do not retain full-object serialization in production.

Do not log every React render.

---

## 7. Preserve all PR #521 behavior

This hotfix must not regress any of the following:

```text
exactly one Device tab
exactly one Profile tab
stable Addon tab keys
legacy one-tab descriptor removal
Addon descriptor dedupe
non-Addon Steam tab preservation
PR #518 generation-scoped descriptor cleanup
Device fixed (Device, null) context
Profile fixed Profile page identity + active AppId target
Profile no-game "No active game." shared unavailable state
Device writable during active game according to shared row authority
PageId + AppId pending identity
Profile stale-AppId retirement
Device pending work independent of Profile target changes
shared invalidation subscriber Set
native ToggleField / SliderField rendering
```

Do not touch the two-tab content architecture unless a concrete hotfix regression requires it.

---

## 8. Tests

## 8.1 `QamFrontendContractTests`

Add/update coverage proving:

1. selection is no longer gated by `initialTabSelectionOwners.has(owner._owner)`;
2. owner-fiber lifetime is not treated as QAM-open lifetime;
3. the native QAM active/inactive lifecycle wrapper preserves the original Steam callback;
4. one inactive -> active transition can request exactly one initial Addon selection;
5. repeated active callbacks/renders do not repeatedly select;
6. active -> inactive re-arms the next open;
7. AppId changes while active do not directly trigger tab selection;
8. state invalidation does not trigger tab selection;
9. DOM/focus/input/timer/polling hacks remain absent;
10. Device/Profile stable keys and descriptors remain unchanged;
11. descriptor cleanup and invalidation subscriber tests from PR #521 remain passing.

Avoid tests that merely assert a guessed Steam prop name unless that prop has been live-verified and is intentionally part of the supported current-Steam contract.

## 8.2 Selection policy unit/source coverage

Where practical, isolate the tiny transition policy enough to prove:

```text
closed + open event -> select
open + rerender -> no select
open + invalidation -> no select
open + user tab change -> no select
open + game AppId change -> no select
close -> re-arm
reopen -> select again
```

Do not create a new framework or class just to unit-test this.

## 8.3 Existing tests

All existing QAM/shared frontend tests must stay green.

At minimum run:

```text
node --check src/SteamInputAddonforClaw.QamHost/Frontend/qam.js

dotnet build SteamInputAddonforClaw.slnx --configuration Debug --no-restore
dotnet build SteamInputAddonforClaw.slnx --configuration Release --no-restore

dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj \
  --configuration Release \
  --no-build

git diff --check
```

Run focused QAM tests during development and the full Release suite before PR completion.

---

## 9. Real-device acceptance — mandatory

This hotfix cannot be accepted from source tests alone because the defect exists specifically in Steam's live QAM visibility/selection lifecycle.

Test on the supported MSI Claw and current Steam GamepadUI.

### 9.1 No active game

Start with a Steam-native tab selected before closing QAM.

Repeat at least three times:

```text
open QAM
-> Device must be the first visible selected tab

select a Steam-native tab manually
-> selection must stay there while QAM remains open

close QAM
open QAM again
-> Device must again be the first visible selected tab
```

This is the exact regression that PR #521 currently fails.

### 9.2 Active game

With a Steam game running:

```text
select a Steam-native tab
close QAM
open QAM
-> Profile must be selected first
-> Profile AppId must equal the current active game
```

Repeat after manually selecting different Steam-native tabs.

### 9.3 User selection while open

With QAM left open:

```text
Addon Device/Profile selected
-> manually select Steam-native tab
-> wait through page refresh/invalidation
-> Steam-native tab must remain selected
```

No Addon refresh or mutation may steal it back.

### 9.4 Game transition while QAM remains open

#### Device selected, then game starts

```text
Device remains selected
Profile content updates to the active game
no forced jump to Profile
```

#### Profile selected, then game exits

```text
Profile remains selected
Profile shows shared "No active game." state
no forced jump to Device
```

#### Steam-native tab selected, then game starts/exits

```text
Steam-native tab remains selected
no Addon selection write occurs
```

Only a close + fresh reopen may reapply the Addon default.

### 9.5 Reinjection / QamHost restart

After script reinjection or QamHost restart:

```text
1 Device tab
1 Profile tab
no legacy tab
fresh QAM open still selects Device/Profile according to AppId
```

No stale descriptor or stale lifecycle wrapper may survive across script generations.

---

## 10. Failure policy

If the exact current-Steam native lifecycle or selection seam cannot be resolved:

```text
keep both Addon tabs present and manually usable
emit one low-noise diagnostic
fail the real-device acceptance for this PR
```

Do **not** compensate with DOM automation or timing retries.

Because the product requirement is now "fresh QAM open must land on the Addon," unresolved native selection is no longer considered a completed implementation merely because manual tab selection still works.

The fallback remains safe, but the PR must stay open until the supported current Steam build is verified.

---

## 11. Explicit non-goals

Do not change:

```text
Full1902 controller ownership
PID1901/PID1902 transitions
HidHide
VIIPER
Xbox360/SteamDeck presentation
Steam/BPM game observation
front-button/WING/OEM1 mapping
QAM tab insertion topology
Device/Profile product projection
profile persistence
Quick Settings mutation adapter
Overlay
fan control
battery limit
LED/vibration
Steam native tabs themselves
```

Do not hide or delete Steam's native tabs in this PR. They may remain accessible when the user intentionally moves to them.

The requirement is only that every **new QAM open** begins in the correct Addon tab.

---

## 12. Overengineering guard

The supported product is:

```text
one Windows user
one interactive session
one visible Steam QAM surface
```

The fix should therefore remain small.

Preferred end state:

```text
one native QAM lifecycle seam
one boolean active/open authority
one exact native tab-selection seam
one open-time selection write
```

Do not introduce abstractions to defend against theoretical instruction-level races.

Only handle realistic lifecycle events:

```text
QAM open
QAM close
QamHost/script reinjection
Steam UI reload/reconnect
bridge failure
active game/no-game state
```

---

## 13. Definition of done

The hotfix is complete only when all of the following are true:

```text
[ ] Device/Profile tabs still appear exactly once.
[ ] Steam native tabs remain available for manual use.
[ ] No-game fresh QAM open always selects Device.
[ ] Active-game fresh QAM open always selects Profile.
[ ] Previous Steam-native last-selected state does not win on reopen.
[ ] Manual Steam-tab selection is respected while QAM stays open.
[ ] AppId changes while open do not force a tab switch.
[ ] Close/reopen re-applies the Addon open policy.
[ ] No DOM click/focus/input automation exists.
[ ] No polling/timer retry exists.
[ ] Existing PageId+AppId mutation safety remains intact.
[ ] Existing descriptor generation cleanup remains intact.
[ ] Focused and full Release tests pass.
[ ] Real-device acceptance on the MSI Claw passes.
```
