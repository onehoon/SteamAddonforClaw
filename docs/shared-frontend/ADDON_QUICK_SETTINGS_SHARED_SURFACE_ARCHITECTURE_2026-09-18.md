# Addon Quick Settings Shared Surface Architecture

> Date: 2026-09-18  
> Status: Implemented architecture authority after the shared-surface convergence sequence
> Baseline reviewed: main at 8c333b1ee4af63e4e7a9f50dd3a4b73a8944c682 after PR #530; PR5 cleanup is recorded here
> Product scope: Standalone Full PID1902  
> CTW integration: not part of this design  
> Implementation sequence: PR #527, #528, #529, #530, and PR5 convergence cleanup

---

## 1. Purpose

Steam Addon for Claw currently has two Quick Settings presentation surfaces:

- Steam native Quick Access Menu through SteamInputAddonforClaw.QamHost.exe;
- Addon-owned Windows Quick Settings Overlay through SteamInputAddonforClaw.Overlay.exe.

The existing Shared Frontend V2 work already solved the most important Device/Profile duplication:

- one Runtime feature authority;
- one QuickSettingsPageSnapshot product contract;
- one Device/Profile projection;
- one closed mutation intent;
- one shared commit/debounce policy;
- surface-specific renderers.

However, the top-level Quick Settings shell is still split.

Current Overlay product shell:

~~~text
Device
Profile
Controller
Shortcut
Setting
~~~

Current Steam QAM Addon surface:

~~~text
one Addon top-level Steam QAM tab
        ↓
native Steam inner Tabs
        ↓
Device
Profile
~~~

The result is that future Quick Settings work can still drift into two independently maintained frontend products:

~~~text
QAM shell / tabs / special pages
+
Overlay shell / tabs / special pages
~~~

The goal of this architecture is:

> Define one Addon Quick Settings product/composition authority and keep only two thin surface renderers.

This does not mean using one visual renderer.

Steam QAM must continue using Steam native React/CommonUI controls.

Windows Overlay must continue using WinUI 3 controls and its existing HWND / controller-capture lifecycle.

The shared layer owns product identity and composition. The surface layer owns pixels, focus/navigation, host lifetime, and admission.

---

## 2. Authority and document precedence

This document extends the current Shared Frontend V2 direction. It does not replace the Full1902 controller authority documents.

For controller ownership and lifecycle, continue to use the authority order defined by:

1. docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
2. docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
3. docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
4. active focused work orders and later addenda

This document is subordinate to those controller/lifecycle authorities.

For Quick Settings frontend architecture, read this document together with:

- docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
- docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
- docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
- docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
- docs/QAM_SINGLE_ADDON_TAB_NATIVE_INNER_TABS_RESEARCH_AND_DESIGN_2026-09-13.md
- docs/work-order/QAM_NATIVE_01_STEAM_NATIVE_SURFACE_AND_INTERACTION_RECOVERY_WORK_ORDER.md
- docs/work-order/QAM_NATIVE_02_DEVICE_PROFILE_STABLE_TABS_WORK_ORDER.md

Where the older Shared Frontend V2 text says not to pre-add Controller / Shortcut / Setting merely because Overlay has them, that remains correct for the Device/Profile QuickSettingsPageSnapshot schema.

This document does not turn those tabs into generic Device/Profile-style pages.

Instead it introduces a higher-level shared Addon Quick Settings shell/composition contract while keeping page-specific contracts narrow and typed.

---

## 3. Current implementation facts

### 3.1 Device/Profile product semantics are already shared

Current source already has:

src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs

with:

~~~text
QuickSettingsPageId
  Device
  Profile

QuickSettingsSectionId
QuickSettingsRowId
QuickSettingsControlKind
QuickSettingsSliderKind
QuickSettingsCommitPolicy
QuickSettingsCommitGroupId
QuickSettingsLinkedSliderConstraint
QuickSettingsPageSnapshot
QuickSettingsMutationIntent
QuickSettingsMutationResult
~~~

Device and Profile therefore already have one product-definition authority.

The existing rule remains:

~~~text
Runtime feature truth
        ↓
QuickSettingsPresentation
        ↓
QuickSettingsPageSnapshot
        ↓
QAM renderer / Overlay renderer
~~~

Do not replace this with a second abstraction.

### 3.2 The five-tab identity is now surface-neutral

Current source:

src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs

defines exactly:

~~~text
Device
Profile
Controller
Shortcut
Setting
~~~

and one exact-order validation contract. Overlay and QAM both consume this shared identity/order
contract; Overlay no longer owns an OverlayTabId product identity.

The user may reorder the five known tabs but may not add, remove, hide, or rename them.

That fixed five-tab product identity is now surface-neutral.

### 3.3 Overlay owns renderer-specific composition only

Current OverlayWindow builds:

- five tab buttons rendered from the shared shell/order contracts;
- local tab selection;
- Device generic Quick Settings page;
- Profile generic Quick Settings page;
- Setting tab-order editor;
- Shortcut 2x2 shell;
- Controller placeholder;
- local logical row selection and scrolling;
- Overlay-only window and animation behavior.

The Device/Profile content is generic, while Setting and Shortcut consume their dedicated shared
contracts. Overlay now owns only renderer-specific layout, navigation, and lifecycle.

### 3.4 QAM now renders the complete shared five-tab shell

The current QAM design uses:

~~~text
Steam native QAM
        ↓
one Addon-owned top-level descriptor
        ↓
Steam native inner Tabs
        ↓
Device / Profile / Controller / Shortcut / Setting
~~~

The QAM renderer intentionally uses Steam native controls such as:

- ToggleField;
- SliderField;
- PanelSection;
- PanelSectionRow;
- Steam native Tabs.

That is a product advantage and must be preserved.

### 3.5 Final ownership after the convergence sequence

The implemented ownership is now:

~~~text
Runtime / Contracts
  shell identity, order, labels
  Device/Profile page metadata and mutation semantics
  Setting tab-order state and mutation result
  Shortcut Slot1-Slot4 read-only product meaning

QAM
  Steam-native React/CommonUI rendering and QAM lifecycle/admission

Overlay
  WinUI rendering, local navigation/geometry, and OQ4 lifecycle/admission

Controller
  shell identity plus renderer-local placeholder only
~~~

No surface owns a second product label/order table, local Device/Profile debounce policy, Shortcut
action schema, or Controller page schema.

### 3.6 The two renderers are intentionally different technologies

QAM:

~~~text
Steam GamepadUI
React
Steam webpack/CommonUI
Steam native focus/navigation
Steam native QAM chrome
~~~

Overlay:

~~~text
WinUI 3
ordinary top-level HWND
OQ4 controller capture
semantic Addon navigation
WinUI controls
~~~

A single visual renderer would require either:

- replacing Steam native controls with custom HTML/CSS; or
- hosting Steam-specific code in Windows Overlay.

Neither is desirable.

---

## 4. Product decision

The new target is:

~~~text
                         Addon Runtime
                              │
                    real feature authorities
                              │
                              ▼
                  Shared Quick Settings product
                              │
          ┌───────────────────┴───────────────────┐
          │                                       │
          ▼                                       ▼
                  Shared shell/composition                  Shared page contracts
                  five tabs / order / labels                Device / Profile / typed special pages
          │                                       │
          └───────────────────┬───────────────────┘
                              ▼
               surface-specific renderer adapters
                    │                     │
                    ▼                     ▼
              Steam QAM               Windows Overlay
              native React             WinUI 3
~~~

The maintenance target is:

~~~text
change a shared tab label once
→ QAM + Overlay change

change shared tab order once
→ QAM + Overlay change

add a shared Device/Profile row once
→ QAM + Overlay change

change a Device/Profile debounce policy once
→ QAM + Overlay change

change Setting tab order state once
→ both surfaces observe the same order
~~~

The target is not:

~~~text
one code file physically renders both surfaces
~~~

The target is:

~~~text
one product/composition authority
+
two small renderer adapters
~~~

---

## 5. What “one frontend” means in this architecture

“One frontend” means one Addon Quick Settings product definition.

It does not mean one UI framework.

Shared:

- tab identities;
- tab labels;
- tab order;
- which product page belongs to which tab;
- Device/Profile sections and rows;
- row labels;
- row order;
- values and options;
- mutation identity;
- commit/debounce policy;
- linked constraints;
- Setting tab-order state;
- Shortcut slot product identity;
- unavailable/placeholder product text where deliberately shared.

Surface-specific:

- Steam React element creation;
- WinUI element creation;
- Steam native component discovery;
- Steam native Tabs integration;
- Overlay Button / StackPanel / Slider construction;
- QAM focus/navigation;
- OQ4 semantic navigation;
- Overlay window geometry;
- Overlay show/hide animation;
- QAM open/close lifecycle;
- QAM admission;
- Overlay capture admission;
- mouse / pointer mechanics;
- Steam theme/chrome;
- WinUI theme/chrome.

---

## 6. Shared shell contract

### 6.1 Promote the existing five-tab identity to a surface-neutral name

The current OverlayTabId is product identity, not truly Overlay-only identity.

Preferred conceptual rename:

~~~text
OverlayTabId
→ AddonQuickSettingsTabId
~~~

with:

~~~text
Device
Profile
Controller
Shortcut
Setting
~~~

Likewise:

~~~text
OverlayTabOrderContract
→ AddonQuickSettingsTabOrderContract
~~~

Do not create a second parallel enum.

The migration should replace the old type in current code and tests.

Because serialized enum member names remain the same, do not add a compatibility manager merely for the C# type rename.

If the persisted property name is currently Overlay-specific, changing that persisted JSON property is optional and should be avoided unless there is a clear maintenance benefit. A type-name cleanup does not justify a settings migration framework.

### 6.2 Add a narrow shell snapshot

Add one small typed read model conceptually equivalent to:

~~~text
AddonQuickSettingsShellSnapshot
├─ Tabs[]
│   ├─ TabId
│   └─ Label
└─ Order
~~~

The exact shape may be simpler if label/order can be represented in one list.

The snapshot must remain closed and typed.

Do not add:

- arbitrary page IDs from strings;
- arbitrary icon URLs;
- arbitrary component names;
- renderer class names;
- JSON layout trees;
- per-surface visibility matrices.

### 6.3 One tab order for both QAM and Overlay

The current persisted five-tab order should become the Addon Quick Settings order, not an Overlay-only order.

Desired behavior:

~~~text
user changes tab order in Setting
        ↓
Runtime persists one order
        ↓
Overlay uses that order
        ↓
QAM uses the same order
~~~

This is real product parity and removes a future duplicate preference.

### 6.4 Tab labels are product-owned

The shared shell projection should own:

~~~text
Device
Profile
Controller
Shortcut
Setting
~~~

or whatever final localized/product labels replace them.

Do not hard-code one copy in qam.js and a second copy in OverlayWindow.

Localization can be added later through the same shared product source when the project introduces it. Do not create a localization framework in this work.

---

## 7. Shared page/composition model

### 7.1 Device and Profile remain on QuickSettingsPageSnapshot

Do not redesign them.

They already have the right model.

Mapping:

~~~text
AddonQuickSettingsTabId.Device
→ QuickSettingsPageId.Device

AddonQuickSettingsTabId.Profile
→ QuickSettingsPageId.Profile
~~~

Both surfaces continue to use their existing generic Toggle/Slider renderer mechanics.

### 7.2 Controller remains a real tab but not a fake generic page

Current Overlay Controller content is a placeholder.

There is no justified shared Controller Quick Settings data contract yet.

Therefore:

~~~text
Controller tab
→ shared shell identity
→ shared placeholder/availability presentation
→ no invented Controller schema
~~~

When real controller Quick Settings controls are approved, add a focused typed contract then.

Do not pre-build:

- generic ControllerFeatureRow;
- arbitrary action rows;
- vibration/LED schemas before their Runtime contracts exist;
- generic form controls for hypothetical features.

### 7.3 Shortcut remains a dedicated typed surface

The current Overlay has a 2x2 Shortcut shell.

Shortcut does not naturally fit the current Device/Profile Toggle/Slider schema.

Do not distort QuickSettingsPageSnapshot by adding generic Action/Grid/Tile kinds merely to fit Shortcut.

The implemented contract is a small dedicated typed model for the current Shortcut product:

~~~text
AddonQuickSettingsShortcutSnapshot
├─ Slot1
├─ Slot2
├─ Slot3
└─ Slot4
~~~

The current contract intentionally contains only four read-only `Unassigned` slots. Assignment and
execution semantics remain out of scope until a real product authority exists.

### 7.4 Setting remains a dedicated typed surface

The current Setting page has one real function:

~~~text
Tab Order
~~~

This is not a Device/Profile feature row.

Keep it as a narrow dedicated typed contract around the existing Runtime-owned tab order.

Do not introduce a generic settings form engine.

For the current product, Setting needs only:

- current five-tab order;
- request to move one known tab up/down;
- authoritative updated order;
- local failure if the mutation fails.

QAM and Overlay should render that same state using native controls appropriate to each surface.

---

## 8. Renderer boundaries

### 8.1 Steam QAM renderer

QAM continues to own:

- Steam webpack discovery;
- Steam QAM renderer patching;
- Steam native Tabs discovery/use;
- ToggleField mapping;
- SliderField mapping;
- PanelSection mapping;
- native Steam focus/navigation;
- QAM-specific close/open behavior;
- bridge plumbing;
- surface-local pending-draft mechanics.

QAM must not own after convergence:

- five-tab product labels;
- five-tab order;
- separate Device/Profile product labels/order;
- Setting tab order truth;
- Shortcut product identity;
- product debounce values;
- feature-specific Runtime method names for shared rows.

### 8.2 Addon Overlay renderer

Overlay continues to own:

- WinUI controls;
- HWND;
- topmost/no-activate behavior;
- monitor/work-area geometry;
- animation;
- logical selected-row visuals;
- scrolling;
- OQ4 semantic navigation;
- outside-click dismissal;
- capture lifecycle.

Overlay must not own after convergence:

- a second copy of five-tab labels;
- a second copy of five-tab order truth;
- Device/Profile product semantics;
- a second Setting product definition;
- a second Shortcut product identity definition.

### 8.3 No WebView2 convergence

Do not replace the existing Overlay with WebView2 merely to physically reuse qam.js.

Do not replace Steam native QAM controls with custom HTML/CSS merely to reuse a Windows web frontend.

That would reduce source duplication but increase product and compatibility risk:

- Steam focus/navigation would need imitation;
- Steam style/theme behavior would need imitation;
- native QAM component reuse would be lost;
- additional WebView lifetime and input integration would be introduced;
- the existing WinUI Overlay investment would be discarded.

The correct convergence layer is product/composition, not pixels.

---

## 9. Transport architecture

Keep the existing process/transport boundaries.

~~~text
Runtime
├─ .Frontend
├─ .Qam
└─ .Overlay
~~~

Do not merge QAM and Overlay into one process.

Do not share one pipe connection between them.

The same typed DTO may be carried over both transports where appropriate.

### QAM

QAM needs a narrow read seam for shared shell state.

Conceptually:

~~~text
CaptureAddonQuickSettingsShell
~~~

Existing QuickSettingsPage capture/mutation remains unchanged.

### Overlay

Overlay already receives tab-order state through its dedicated lifecycle/preferences transport.

After the shell contract is promoted to surface-neutral identity, reuse the same authoritative order.

Do not send duplicate shell truth over two independent Overlay messages unless current transport structure requires it.

Prefer adapting existing tab-order publication rather than adding parallel state.

---

## 10. Surface admission and lifecycle remain separate

Shared frontend composition must not merge lifecycle authority.

### QAM admission

QAM exists only when its Steam/GamepadUI host is admitted by current product policy.

Its mutation admission remains QAM-specific.

The shared shell does not start Steam, switch UI mode, or change QamHost lifetime.

### Overlay admission

Overlay continues to require its existing Ready / Visible / OQ4 capture contract for editable mutations.

Overlay capture remains a controller-safety mechanism unique to the Windows Overlay.

QAM must never acquire OQ4 Overlay capture merely because both surfaces share product composition.

### Main UI

The desktop management UI remains separate.

Do not force the full Main UI into the Quick Settings shell contract.

---

## 11. Initial tab selection remains surface-specific

Tab identity/order are shared.

Initial selection does not need to be identical.

This distinction is intentional.

Possible current behavior:

~~~text
QAM with active game
→ Profile can be the preferred inner tab

QAM with no active game / BPM Device context
→ Device can be preferred

Overlay show
→ current Overlay product policy may reset to first configured tab
~~~

Do not create a global selected-tab authority.

Selected tab is transient presentation state.

Only product order/identity is shared.

This avoids unnecessary cross-process UI synchronization.

---

## 12. Refresh and invalidation

Continue event-driven behavior.

No polling is introduced.

### Device/Profile

Use the existing:

- initial capture;
- StateInvalidated refresh;
- mutation-result authoritative page;
- pending-draft preservation rules.

### Shell/order

Tab-order changes should publish through current settings invalidation / explicit state response paths.

QAM and Overlay should converge to the same authoritative order after a successful mutation.

There is no need for an epoch/barrier system.

A normal failed request returns failure and keeps/refreshes authoritative state.

---

## 13. Failure behavior

### QAM renderer-specific failure

If Steam native Tabs or a required native component cannot be uniquely resolved:

~~~text
QAM integration fails closed locally
Runtime survives
Overlay survives
controller lifecycle unchanged
~~~

Do not fall back to a custom HTML clone.

### Overlay renderer-specific failure

If Overlay WinUI rendering/window fails:

~~~text
existing Overlay session-loss/capture cleanup applies
Runtime survives
QAM survives
Full1902 controller safety remains authoritative
~~~

### Shared product projection failure

If one shared page cannot be captured:

- expose unavailable state for that page;
- do not tear down the other surface;
- do not tear down controller Runtime;
- healthy pages remain usable.

### Shell snapshot failure

If QAM cannot retrieve the shared shell snapshot:

preferred fail-closed behavior is to preserve the last known in-process safe shell only if current architecture already has such state for the same session; otherwise do not fabricate a second product order.

Do not add a persistent cache solely for this.

Overlay can continue using its already-admitted authoritative transport state.

---

## 14. Full1902 safety invariants

This architecture must not change:

- Center M Enabled/Disabled authority;
- PID1901 / PID1902 desired-state rules;
- reboot-bound authority transition;
- DirectInput ownership;
- HidHide Applications baseline;
- exact PID1902 hidden-device policy;
- VIIPER server/bus ownership;
- Xbox360 / SteamDeck presentation ownership;
- Steam/BPM presentation selection;
- PnP loss/re-enumeration recovery;
- sleep / hibernate / resume;
- restart / shutdown teardown;
- fail-close behavior;
- OQ4 neutral publication / release gate.

Quick Settings frontend work is presentation/application state only.

No frontend tab, page, or visibility fact may become controller authority.

---

## 15. Overengineering guardrails

Do not create for this convergence:

- a generic UI framework;
- plugin/provider model;
- reflection-based renderer;
- JSON page DSL;
- arbitrary control registry;
- schema-to-XAML engine;
- schema-to-React engine;
- cross-framework component abstraction;
- new frontend manager service;
- selected-tab synchronization service;
- renderer authority state machine;
- generic page capability matrix;
- generic navigation graph;
- lifecycle epoch/barrier framework;
- polling loop.

Use small closed typed contracts.

If a page has special semantics, use one special typed contract.

The goal is not maximum code reuse.

The goal is:

> one product authority, one state meaning, one mutation meaning, and thin surface adapters.

---

## 16. Expected final source ownership

Conceptually:

~~~text
Contracts / Runtime
│
├─ AddonQuickSettingsTabId
├─ AddonQuickSettingsTabOrderContract
├─ AddonQuickSettingsShellSnapshot
│
├─ QuickSettingsPageSnapshot
│   ├─ Device
│   └─ Profile
│
├─ Setting tab-order typed state/mutation
│
└─ Shortcut typed state/mutation
    only when real shortcut product semantics exist

QamHost
│
├─ Steam QAM injection
├─ native inner Tabs renderer
├─ Device/Profile generic native renderer
├─ Setting native renderer
└─ Shortcut native renderer

Overlay
│
├─ WinUI window/lifecycle
├─ WinUI tab strip renderer
├─ Device/Profile generic WinUI renderer
├─ Setting WinUI renderer
└─ Shortcut WinUI renderer
~~~

Controller remains a shared shell tab with a shared unavailable/placeholder state until real product controls exist.

---

## 17. Five-PR implementation plan

### PR 1 — Shared shell identity and Runtime contract foundation

Goal:

Promote the existing five-tab Overlay identity into the Addon Quick Settings product contract without changing visible behavior.

Main changes:

- rename/promote OverlayTabId to AddonQuickSettingsTabId;
- rename/promote OverlayTabOrderContract accordingly;
- update Runtime settings/persistence/Overlay transport consumers;
- add AddonQuickSettingsShellSnapshot or equivalent narrow typed read model;
- centralize five labels and canonical order;
- add the smallest Runtime/frontend capture seam QAM can consume;
- preserve existing persisted order semantics;
- preserve current Overlay UI behavior exactly.

Likely areas:

- src/SteamInputAddonforClaw.Contracts
- src/SteamInputAddonforClaw/Settings
- src/SteamInputAddonforClaw/Frontend
- src/SteamInputAddonforClaw.FrontendTransport
- Overlay tab-state code
- tests

Non-goals:

- no visible QAM change;
- no QAM five-tab UI yet;
- no Setting/Shortcut renderer migration;
- no Full1902 change.

Why separate:

This PR establishes one identity authority first so later QAM work cannot create a second five-tab definition.

### PR 2 — QAM shared five-tab native inner shell

Goal:

Make the existing single Addon top-level QAM tab render the same five product tabs/order/labels as Overlay.

Desired topology:

~~~text
Steam QAM
        ↓
one Addon top-level tab
        ↓
Steam native inner Tabs
        ↓
Device | Profile | Controller | Shortcut | Setting
~~~

Main changes:

- qam.js captures shared shell snapshot;
- native Tabs use shared order/labels;
- Device/Profile mount the existing generic QuickSettingsPageSnapshot renderers;
- Controller/Shortcut/Setting initially use narrow shared placeholder/admission content where their functional renderer is not yet migrated;
- preserve exact one Addon top-level descriptor;
- preserve native Steam Tabs/components;
- preserve current QAM initial-tab context behavior;
- no HTML/CSS imitation.

Tests:

- exactly five inner tabs from shared shell;
- order mutation in test snapshot changes QAM order without JS product edit;
- labels come from payload;
- Device/Profile remain functional;
- one Addon top-level descriptor only;
- no duplicate hard-coded five-tab array in qam.js.

### PR 3 — Shared Setting tab-order product + dual renderer parity

Goal:

Move the current Overlay Setting / Tab Order behavior to one surface-neutral typed product contract and render it in both surfaces.

Main changes:

- Runtime-owned tab order remains authority;
- promote current Overlay-specific tab-order state/mutation naming to Addon Quick Settings naming where useful;
- Overlay Setting binds the shared state instead of an Overlay-only product definition;
- QAM Setting renders the same five rows/order and move operations using Steam native controls;
- authoritative readback updates both surfaces;
- no selected-tab global state.

Tests:

- same order shown on both renderers from one snapshot;
- one move request reaches the same Runtime mutation;
- boundary move emits no mutation;
- invalid order rejected centrally;
- QAM/Overlay local failure does not alter Runtime truth.

### PR 4 — Shared Shortcut composition + dual renderer parity

Goal:

Stop Shortcut from becoming two separate products.

Current product is only a 2x2 shell/foundation. Do not invent future actions.

Main changes:

- introduce only the smallest surface-neutral Shortcut slot identity/state required by current product;
- reuse existing four-slot meaning;
- Overlay renders its current 2x2 layout from shared slot state;
- QAM renders the same four slots using a Steam-native layout;
- future shortcut action mapping can extend this typed contract in a later focused feature PR.

If current Shortcut remains intentionally non-functional at implementation time, this PR may be limited to:

- shared slot identities/labels;
- identical unavailable/placeholder semantics;
- no fake mutation API.

Tests:

- four known slots exactly once;
- both surfaces receive the same slot identity/order;
- no generic tile/plugin framework;
- no arbitrary action string dispatch.

### PR 5 — Convergence cleanup and parity acceptance

Goal:

Finish the architectural migration and remove remaining product-definition duplication.

Main changes:

- remove obsolete Overlay-only five-tab product constants/labels;
- remove obsolete QAM-only Addon inner-tab product constants/labels;
- remove temporary placeholder bridge paths that are no longer needed;
- make shared shell/order the only product authority;
- keep Controller as a shared placeholder unless real controller controls exist;
- update architecture/work-order references;
- add final contract tests that assert renderer parity at the product level;
- hardware acceptance for QAM and Overlay independently.

Acceptance:

~~~text
same Runtime shell snapshot
→ same tab identities/order/labels in both surfaces

same Device page snapshot
→ same product rows/values in both surfaces

same Profile page snapshot
→ same product rows/values in both surfaces

same Setting state
→ same tab-order product in both surfaces

same Shortcut state
→ same four-slot product in both surfaces
~~~

Pixels are allowed to differ because one renderer is Steam native and one is WinUI.

---

## 18. Implementation sequence and status

Completed implementation:

~~~text
5 PRs
~~~

The implementation followed this architecture document as five independently reviewable PRs.

Completed order:

~~~text
PR1 shared identity/contract
→ PR2 QAM five-tab shell
→ PR3 Setting parity
→ PR4 Shortcut parity
→ PR5 cleanup + acceptance
~~~

PR5 removed the remaining QAM debounce fallback, added final parity guardrails, and refreshed the
living architecture documents. Historical work orders remain unchanged.

---

## 19. Hardware acceptance focus

This architecture does not require a new all-at-once controller lifecycle test campaign.

Focused UI acceptance is enough, while preserving existing Full1902 regression tests.

QAM acceptance:

- one Addon top-level tab;
- five shared inner tabs;
- native Steam controls remain native;
- Device/Profile mutations still work;
- Setting order changes work;
- close/reopen remains usable;
- no duplicate injected descriptors.

Overlay acceptance:

- five shared tabs;
- same order/labels;
- Device/Profile mutations still work;
- Setting order changes work;
- Shortcut shell parity;
- OQ4 capture/release unchanged;
- no input leaks on close;
- no presentation switch caused by UI visibility.

Cross-surface acceptance:

- change tab order once;
- close/reopen both surfaces;
- both show the same authoritative order.

---

## 20. Future feature rule

After convergence, a new Quick Settings feature should be added according to its real product shape.

If it is a normal Toggle/Slider row shared by QAM and Overlay:

~~~text
extend QuickSettingsPageSnapshot projection
+ central mutation adapter
→ no shell rewrite
→ no per-surface product definition
~~~

If it is a special page such as Controller or Shortcut:

~~~text
define the smallest dedicated typed product contract
→ QAM adapter
→ Overlay adapter
~~~

Do not force every future page into QuickSettingsPageSnapshot.

The closed typed model should grow only from real features.

---

## 21. Final architecture statement

The final rule is:

> Runtime owns truth.  
> Shared Quick Settings owns product identity and composition.  
> QAM and Overlay are renderer/admission hosts, not separate products.

Concretely:

~~~text
one five-tab product
one tab order
one set of labels
one Device definition
one Profile definition
one Setting meaning
one Shortcut meaning

but

two renderers
two lifecycle/admission paths
two visual technologies
~~~

This gives the maintenance benefit of one frontend product without sacrificing Steam-native QAM quality or the proven WinUI/OQ4 Windows Overlay lifecycle.
