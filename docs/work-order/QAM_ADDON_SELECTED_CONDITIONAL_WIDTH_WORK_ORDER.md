# Work Order — Addon-Selected QAM Conditional Width

> **Date:** 2026-09-20  
> **Status:** Ready for implementation  
> **Reviewed production baseline:** `main` at `931977f1e6a11ee63310fab6b20b3e91a88156d2` after PR #534  
> **Architecture authority:** standalone Full PID1902  
> **Primary QAM design authority:** `docs/QAM_SINGLE_ADDON_TAB_NATIVE_INNER_TABS_RESEARCH_AND_DESIGN_2026-09-13.md`  
> **Previous PR:** PR #534 — causal fresh-open Addon top-level selection  
> **Scope:** widen Steam QAM only while the single Addon top-level tab is selected; restore Steam's normal QAM width when any Steam-native top-level tab is selected.

---

## 1. Goal

Make the Addon Quick Settings surface visually closer to the standalone Overlay by giving the Addon top-level QAM tab more horizontal space.

Target behavior:

```text
Steam QAM + Addon top-level tab selected
→ QAM outer panel width = 400 CSS px

Steam QAM + any Steam-native top-level tab selected
→ Steam's original width/style

Addon selected again
→ 400 CSS px again
```

This must be conditional to the top-level Addon selection.

Do **not** globally widen Steam QAM.

Do **not** change the width of Steam-native pages.

---

## 2. Why 400 CSS px

The existing standalone Overlay reference geometry is:

```text
Overlay width       = 400 DIP
1920 × 1200 @ 150% = 600 physical px
```

For Chromium/GamepadUI on the same Windows scaling, a QAM width of approximately:

```text
400 CSS px
```

provides the desired visual parity target.

Initial production constant:

```javascript
const ADDON_QAM_WIDTH_PX = 400;
```

Do not add a user-facing width setting in this PR.

If real-device acceptance proves 400 is materially wrong, adjust this one constant before merge rather than introducing a setting/range.

---

## 3. Required reading before editing

Read:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/QAM_SINGLE_ADDON_TAB_NATIVE_INNER_TABS_RESEARCH_AND_DESIGN_2026-09-13.md

docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md

docs/work-order/QAM_FRESH_OPEN_ADDON_TAB_SELECTION_CAUSAL_INTENT_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR2_QAM_FIVE_TAB_NATIVE_SHELL_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR5_CONVERGENCE_CLEANUP_PARITY_ACCEPTANCE_WORK_ORDER.md
```

Inspect current production source:

```text
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js

tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
```

This PR should normally touch only those two files.

If another production file is needed, justify the need in the PR description before expanding scope.

---

## 4. External implementation evidence

This work is supported by current public Steam/Deck UI implementations.

### 4.1 `PanelOuterNav` is the QAM width boundary

Public Deck CSS modifications directly change:

```css
.quickaccessmenu_PanelOuterNav_<hash> {
    width: 100%;
    max-width: unset;
}
```

to create a wider/full-width QAM.

Another public GamepadUI theme defines the same semantic class with:

```css
.quickaccessmenu_PanelOuterNav_<hash> {
    max-width: 300px;
}
```

These are reference implementations only; the exact hashed class suffix is Steam-build-dependent.

Conclusion:

```text
PanelOuterNav
→ correct semantic width boundary
```

### 4.2 Decky resolves the class module semantically

`decky-frontend-lib` does not hard-code the minified hash.

Its current static-class mapping defines a Quick Access class module containing:

```text
PanelOuterNav
QuickAccessMenu
Title
BatteryDetailsLabels
...
```

and resolves the module using stable semantic keys equivalent to:

```javascript
m.Title &&
m.QuickAccessMenu &&
m.BatteryDetailsLabels
```

This is the reference pattern for this PR.

Do not add Decky or a theme package as a dependency.

---

## 5. Non-goals

Do not change:

- PR #534 causal Addon-first selection semantics;
- Runtime → QamHost acknowledgement flow;
- `MenuStore.m_eOpenSideMenu` handling;
- SteamDeck Quick Access pulse behavior;
- Addon top-level descriptor identity;
- shared five inner tabs;
- Device/Profile Runtime contracts;
- Setting tab-order contract;
- Shortcut contract;
- Overlay protocol;
- frontend protocol;
- PID1901/PID1902;
- HidHide;
- VIIPER;
- rumble;
- suspend/resume;
- controller presentation;
- QamHost process lifecycle.

This is QAM presentation-only work.

---

## 6. No protocol changes

Expected versions remain:

```text
FrontendTransportProtocol = 34
OverlayTransportProtocol  = 8
```

Do not add an RPC or notification.

Do not bump either protocol.

The selection event required by this PR already exists inside Steam:

```text
MenuStore.OpenQuickAccessMenu(key, false)
```

---

## 7. Do not hard-code Steam's hashed CSS class

Forbidden:

```javascript
"quickaccessmenu_PanelOuterNav_2BB6u"
```

or any other current/minified/hash-derived literal.

Steam may change that suffix independently of the semantic QAM structure.

Resolve the current class module from webpack using stable semantic member names.

---

## 8. Add an optional Quick Access class resolver

Keep width discovery independent from mandatory QAM native-control discovery.

A failure to resolve width styling must **not** prevent Addon tab injection.

Suggested shape:

```javascript
function findQuickAccessMenuClasses(webpackRequire) {
  const matches = [];

  for (const moduleExports of collectSearchableModules(webpackRequire)) {
    for (const candidate of Object.values(moduleExports)) {
      if (!candidate || typeof candidate !== "object") continue;

      if (typeof candidate.Title !== "string" ||
          typeof candidate.QuickAccessMenu !== "string" ||
          typeof candidate.BatteryDetailsLabels !== "string" ||
          typeof candidate.PanelOuterNav !== "string") {
        continue;
      }

      matches.push(candidate);
    }
  }

  const unique = [...new Set(matches)];

  if (unique.length !== 1) {
    logOnce(
      "qamWidthClassModule",
      `QAM width class discovery unavailable; expected one Quick Access class module, found ${unique.length}.`);
    return null;
  }

  return {
    PanelOuterNav: unique[0].PanelOuterNav,
  };
}
```

Equivalent code is fine.

Required behavior:

```text
exactly one semantic class module
→ enable width enhancement

zero or multiple matches
→ keep normal Steam width
→ Addon QAM remains fully usable
```

Do not broaden discovery until "something matches".

---

## 9. Keep width state renderer-local

Add only the minimum generation-local state required by this presentation feature.

Conceptually:

```javascript
state.addonQamWidthActive = false;
state.qamWidthClassNames = null;
state.qamWidthSelectionPatch = null;
state.qamWidthOriginalStyles = new WeakMap();
```

Exact field names may vary.

Do not persist this state.

Do not expose it through Runtime.

Do not create:

- QamWidthManager;
- QamStyleService;
- global UI theme registry;
- width protocol;
- width preference;
- selection epoch.

---

## 10. Observe top-level selection through the already verified native selection path

PR #534 and the current QAM implementation already use:

```javascript
MenuStore.OpenQuickAccessMenu(key, false)
```

as the verified native top-level selection write.

Live inspection established that Steam's own top-level tab handler passes its descriptor key through this same method.

Use that one path to observe:

```text
key === ADDON_TAB_KEY
→ Addon width active

any other key
→ Addon width inactive
```

Do not reintroduce:

- `args[0].visible`;
- DOM click listeners;
- focus inference;
- polling;
- MutationObserver;
- timers;
- selected-tab guessed props.

---

## 11. Install one narrow `OpenQuickAccessMenu` wrapper

Patch only the current verified `MenuStore.OpenQuickAccessMenu` method.

The wrapper must:

1. preserve the original method and `this`;
2. set the width-selection flag **before** invoking the original method;
3. call the original exactly once;
4. restore the previous flag if the original throws;
5. return the original result unchanged.

Setting the flag before the original call is important because Steam may synchronously schedule/render the selected page inside the original method.

Illustrative shape:

```javascript
function installAddonQamWidthSelectionHook() {
  if (state.qamWidthSelectionPatch) return true;

  const authority = resolveNativeQamMenuAuthority();
  const menuStore = authority?.menuStore;
  if (!menuStore || typeof menuStore.OpenQuickAccessMenu !== "function")
    return false;

  const hadOwn = Object.prototype.hasOwnProperty.call(
    menuStore,
    "OpenQuickAccessMenu");

  const original = menuStore.OpenQuickAccessMenu;

  function wrappedOpenQuickAccessMenu(key, ...args) {
    const previous = state.addonQamWidthActive === true;
    state.addonQamWidthActive = key === ADDON_TAB_KEY;

    try {
      return original.apply(this, [key, ...args]);
    } catch (error) {
      state.addonQamWidthActive = previous;
      throw error;
    }
  }

  menuStore.OpenQuickAccessMenu = wrappedOpenQuickAccessMenu;

  state.qamWidthSelectionPatch = {
    menuStore,
    original,
    wrapped: wrappedOpenQuickAccessMenu,
    hadOwn,
  };

  return true;
}
```

The exact function form may differ.

---

## 12. Extend the existing native menu resolver instead of creating a second store path

Current `resolveNativeQamMenuAuthority()` already resolves the verified MenuStore.

Extend its returned object if needed so the width hook can reuse the same store:

```javascript
return {
  menuStore,
  isQuickAccessOpen: () =>
    menuStore.m_eOpenSideMenu === QUICK_ACCESS_SIDE_MENU_ID,

  selectAddon: () => {
    menuStore.OpenQuickAccessMenu(ADDON_TAB_KEY, false);
  },
};
```

Do not create another independent path such as:

```javascript
SteamUIStore...GamepadUIMainWindowInstance...
```

while retaining the current one.

One verified MenuStore authority is the project rule.

---

## 13. Install the width selection hook before PR #534 consumes the Addon-first request

Current `ensureAddonTabs(...)` ends by calling:

```javascript
tryConsumeAddonSelectionRequest();
```

The width hook must be installed before that call.

Target ordering:

```text
ensureAddonTabs
→ Addon descriptor present
→ width selection hook installed
→ PR #534 pending Addon-first request consumed
→ OpenQuickAccessMenu(ADDON_TAB_KEY, false)
→ wrapper marks Addon width active
→ Steam renders Addon
→ width patch sees active=true
```

Do not change PR #534's causal intent/ack flow.

---

## 14. Patch only the `PanelOuterNav` React node

Do not inject a global CSS file.

Do not change all QAM classes.

Reuse the existing bounded React walker to find the exact current `PanelOuterNav` node in the outer QAM render result.

Suggested helper:

```javascript
function hasExactClass(node, className) {
  const value = node?.props?.className;
  if (typeof value !== "string") return false;

  return value
    .split(/\s+/)
    .filter(Boolean)
    .includes(className);
}
```

Then:

```javascript
function applyAddonQamWidth(result) {
  const className = state.qamWidthClassNames?.PanelOuterNav;
  if (!className) return;

  const found = findReactNode(
    result,
    node => hasExactClass(node, className));

  const target = found.node;
  if (!target?.props) return;

  // apply/restore as defined below
}
```

Use the existing bounded walker.

Do not add a second generic React crawler.

---

## 15. Change only `width` and `maxWidth`

Addon active:

```javascript
target.props.style = {
  ...(target.props.style || {}),
  width: `${ADDON_QAM_WIDTH_PX}px`,
  maxWidth: `${ADDON_QAM_WIDTH_PX}px`,
};
```

Do not change in this PR:

- height;
- margin;
- padding;
- transform;
- animation;
- background;
- tab size;
- font;
- color;
- scrollbar;
- inner layout.

Those belong to the later Overlay/QAM visual-parity work.

---

## 16. Preserve Steam's original inline style

Never assume `target.props.style` is empty.

Use one generation-local `WeakMap` to remember the original inline style object for a target node that is actually modified.

Conceptually:

```javascript
state.qamWidthOriginalStyles ??= new WeakMap();

if (state.addonQamWidthActive) {
  if (!state.qamWidthOriginalStyles.has(target)) {
    state.qamWidthOriginalStyles.set(
      target,
      target.props.style);
  }

  target.props.style = {
    ...(target.props.style || {}),
    width: "400px",
    maxWidth: "400px",
  };

  return;
}

if (state.qamWidthOriginalStyles.has(target)) {
  target.props.style =
    state.qamWidthOriginalStyles.get(target);

  state.qamWidthOriginalStyles.delete(target);
}
```

Equivalent implementation is acceptable.

If Steam creates a new React node on the inactive render, it naturally carries Steam's unmodified style and requires no restoration entry.

Do not attempt to reconstruct Steam's default width numerically.

---

## 17. Apply the width patch from the already patched outer QAM renderer

Current outer renderer patch already receives:

```javascript
const result = originalType.apply(this, args);
```

and then performs Addon QAM augmentation.

Call the width helper on that current render result.

Conceptually:

```javascript
const result = originalType.apply(this, args);
if (!state.installed) return result;

patchTabsProducer(result);
applyAddonQamWidth(result);

return result;
```

The exact position relative to `patchTabsProducer(result)` may follow current code structure, but:

- it must operate on the current render result;
- it must not trigger another render;
- it must not loop/retry.

---

## 18. Do not infer QAM open/close for width

Width selection does not need a QAM lifecycle state.

Reason:

```text
Addon selected
→ width flag true

Steam tab selected
→ width flag false
```

If QAM closes while Addon is selected, the hidden QAM retaining the Addon width state is harmless.

When it reopens:

- PR #534 Addon-caused open selects Addon through `OpenQuickAccessMenu`, keeping/writing the correct active state;
- if the user had switched to a Steam-native tab before closing, the wrapper already set the state false;
- Steam's remembered selection remains aligned with the last observed top-level selection.

Do not add close/open monitoring only to clear a cosmetic hidden state.

---

## 19. Manual / non-Addon QAM opens remain untouched

PR #534 deliberately does not globally hijack externally opened QAM.

This PR must preserve that behavior.

Width follows actual top-level selection events only.

Examples:

```text
manual QAM open + remembered Steam tab
→ stock width

manual QAM open + user selects Addon
→ 400 px

manual QAM open + user leaves Addon
→ stock width
```

Do not force Addon selection from the width feature.

---

## 20. Cleanup the native method patch conservatively

On uninstall/reinstall, restore `OpenQuickAccessMenu` only if the method still equals our wrapper.

Do not overwrite another patch installed after ours.

Also preserve whether the original method was an own property.

Illustrative cleanup:

```javascript
function uninstallAddonQamWidthSelectionHook() {
  const patch = state.qamWidthSelectionPatch;
  state.qamWidthSelectionPatch = null;
  state.addonQamWidthActive = false;

  if (!patch) return;

  if (patch.menuStore.OpenQuickAccessMenu !== patch.wrapped)
    return;

  if (patch.hadOwn)
    patch.menuStore.OpenQuickAccessMenu = patch.original;
  else
    delete patch.menuStore.OpenQuickAccessMenu;
}
```

Do not introduce a generic method-patch manager.

---

## 21. Generation cleanup

On `install()` reset:

```text
addonQamWidthActive = false
qamWidthClassNames = newly resolved current-generation class names
qamWidthSelectionPatch = null
qamWidthOriginalStyles = new WeakMap()
```

On `uninstall()`:

1. restore the MenuStore method if still owned;
2. clear width state;
3. clear class references;
4. clear original-style tracking;
5. continue existing descriptor/hook cleanup.

On GamepadUI document replacement/reinjection, the existing generation lifecycle remains authoritative.

Do not persist Steam class names across a document generation.

---

## 22. Fail-open policy

### Class module unresolved

```text
Addon tab works normally
QAM stays stock width
one low-noise log
```

### `PanelOuterNav` node not found in one render

```text
return current Steam render unchanged
do not throw
do not retry-loop
```

A low-noise `logOnce` is acceptable if useful for hardware validation.

### MenuStore selection hook unavailable

```text
Addon selection from PR #534 still works
QAM width remains stock
```

Never make width enhancement a prerequisite for QAM usability.

---

## 23. Tests — semantic class discovery

Update `QamFrontendContractTests.cs`.

Guard that the implementation:

- resolves the Quick Access class module semantically;
- requires `Title`;
- requires `QuickAccessMenu`;
- requires `BatteryDetailsLabels`;
- requires `PanelOuterNav`;
- fails closed unless the result is unique;
- does not contain a literal hashed class such as `quickaccessmenu_PanelOuterNav_2BB6u`.

Do not build a generic CSS-module framework for this test.

---

## 24. Tests — selection tracking

Add source-contract coverage proving:

```text
OpenQuickAccessMenu wrapper exists
key === ADDON_TAB_KEY → width active
other key → width inactive
state change occurs before original.apply(...)
original is called exactly once
throw path restores previous width flag
```

Also prove:

```text
ensureAddonTabs
→ installs width hook
→ then calls tryConsumeAddonSelectionRequest()
```

This protects PR #534's automatic first-open path.

---

## 25. Tests — width patch

Guard:

```text
ADDON_QAM_WIDTH_PX = 400
PanelOuterNav exact semantic class is the target
only width/maxWidth are added
WeakMap preserves original style
inactive path restores the original style
```

Do not require exact formatting.

Do not assert a minified class hash.

---

## 26. Tests — cleanup

Guard that uninstall:

- owns a dedicated width-hook cleanup;
- restores the original `OpenQuickAccessMenu` only if our wrapper is still installed;
- respects original own-property state;
- resets `addonQamWidthActive`;
- clears generation-local class/style state.

Existing QAM descriptor cleanup tests must remain green.

---

## 27. Tests — keep speculative mechanisms absent

Retain/extend guards so this width feature contains no:

```text
setInterval
MutationObserver
DOM click
focus()
synthetic input
width RPC
new protocol
```

Do not ban the existing Quick Settings debounce `setTimeout` globally; scope any source assertion to the width implementation.

---

## 28. Validation commands

Run:

```text
node --check src/SteamInputAddonforClaw.QamHost/Frontend/qam.js

dotnet build SteamInputAddonforClaw.slnx --no-restore -v:minimal

dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj

git diff --check
```

Expected:

- JavaScript syntax passes;
- build passes;
- all tests pass;
- frontend protocol remains 34;
- Overlay protocol remains 8.

---

## 29. Required hardware acceptance

Use MSI Claw at the primary reference configuration:

```text
1920 × 1200
Windows scale 150%
```

### Case A — PR #534 automatic open

Start with QAM closed.

Press the Addon-mapped SteamQuickAccess button.

Expected:

```text
QAM opens
Addon top-level tab selected
QAM width becomes ~400 CSS px
physical width is approximately the 600 px Overlay reference
```

No obvious one-frame persistent stock-width layout after selection settles.

### Case B — Addon → Steam-native tab

While QAM remains open:

```text
select any Steam-native top-level tab
```

Expected:

```text
QAM returns immediately to Steam's normal width
native page layout is unchanged
```

### Case C — Steam-native → Addon

Select Addon again.

Expected:

```text
QAM returns to ~400 CSS px
inner Device/Profile/Controller/Shortcut/Setting shell remains usable
```

### Case D — close/reopen from Addon

Close QAM while Addon is selected.

Open again through the Addon SteamQuickAccess action.

Expected:

```text
PR #534 selects Addon
width is 400 px
no duplicate selection
no repeated width growth
```

### Case E — close/reopen from native tab

Select a Steam-native tab, close QAM, and open through a non-Addon/manual source if practical.

Expected:

```text
Steam native remembered tab
stock Steam width
```

Then select Addon manually:

```text
width becomes 400 px
```

### Case F — repeated switching

Repeat:

```text
Addon
→ Steam tab
→ Addon
→ Steam tab
```

at least several times.

Expected:

- no accumulating width;
- no tab duplication;
- no React error screen;
- no broken focus/navigation;
- no stale 400px width on Steam-native pages.

### Case G — QamHost / GamepadUI reinjection

Exercise a normal QamHost restart or GamepadUI document reinjection if available.

Expected:

- old method wrapper is retired;
- no duplicated wrapper;
- no permanent globally widened QAM;
- next normal Addon selection can enable 400px width again.

---

## 30. Verify existing shared pages after width change

At least enter:

```text
Device
Profile
Controller
Shortcut
Setting
```

inside the wider Addon surface.

Verify:

- no `React is not defined`;
- native Tabs still navigate;
- Setting sliders remain usable;
- Shortcut read-only content renders;
- Device/Profile controls are not clipped;
- no new horizontal scrollbar.

This PR must not change their product behavior.

---

## 31. Logging

Keep logs low-noise.

Recommended optional logs:

```text
QAM Addon width class resolved. PanelOuterNav=<semantic class>
QAM Addon width selection hook installed.
QAM Addon width unavailable; Quick Access class module not uniquely resolved.
```

Do not log every Addon/native tab switch unless temporarily needed for hardware validation.

Do not retain broad object dumps.

---

## 32. Acceptance criteria

The PR is complete when all are true:

1. Addon top-level QAM renders at 400 CSS px.
2. Steam-native top-level pages retain stock Steam width.
3. Switching Addon ↔ Steam pages updates width immediately and repeatedly.
4. PR #534 automatic Addon-first selection activates the wider width.
5. Width discovery uses semantic Quick Access class-module keys.
6. No hashed Steam CSS class is hard-coded.
7. Only `PanelOuterNav` width/maxWidth are modified.
8. Original inline style is preserved/restored rather than reconstructed.
9. The width feature reuses the current verified MenuStore authority.
10. No renderer visibility/open lifecycle inference is reintroduced.
11. No polling/timers/MutationObserver/DOM click/focus hack is introduced.
12. MenuStore wrapper is restored conservatively on uninstall.
13. Width failure is fail-open and never breaks QAM.
14. Frontend protocol stays v34.
15. Overlay protocol stays v8.
16. Full1902 controller lifecycle code is untouched.
17. Automated tests pass.
18. Hardware Cases A-G pass.

---

## 33. Review guidance

Treat this as a narrow renderer presentation change.

### Blocking

Examples:

- globally widening all Steam QAM pages;
- hard-coding a hashed Steam class;
- using a second guessed MenuStore authority;
- width state updated only after the original selection call, allowing the synchronous selected render to miss it;
- failing to restore Steam-native width after the user leaves Addon;
- clobbering another patch during uninstall;
- making class/style discovery required for Addon tab injection;
- reintroducing guessed visibility/polling;
- changing protocol or Full1902 ownership unnecessarily.

### Non-blocking

Examples:

- exact helper naming;
- whether 400 is represented as a number constant plus `px` formatting;
- minor log wording.

### Theoretical / do not block

Do not add epochs, barriers, observers, document managers, style managers, or retry systems for pathological renderer interleavings.

The supported invariant is:

```text
verified top-level selection call says Addon
→ next/current QAM render uses 400px PanelOuterNav

verified top-level selection call says non-Addon
→ next/current QAM render uses Steam's original PanelOuterNav style
```

Keep the implementation proportional to that invariant.
