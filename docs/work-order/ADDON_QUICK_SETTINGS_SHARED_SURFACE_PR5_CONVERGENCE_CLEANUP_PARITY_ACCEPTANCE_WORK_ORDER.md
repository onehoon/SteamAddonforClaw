# Work Order - Addon Quick Settings Shared Surface PR5: Convergence Cleanup + Final Parity Acceptance

> Date: 2026-09-19
> Status: Ready for implementation
> Reviewed production baseline: main at 8c333b1ee4af63e4e7a9f50dd3a4b73a8944c682 after PR #530
> Shared-surface sequence: PR #527 -> #528 -> #529 -> #530 -> this PR5
> Architecture authority: docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md
> Full1902 authority: docs/Full 1902 Implementation/README.md and referenced Full1902 documents
> Product scope: Standalone Full PID1902
> CTW integration: Out of scope

---

## 1. Goal

PR1-PR4 already established the intended ownership:

~~~text
Shared shell
  AddonQuickSettingsTabId
  AddonQuickSettingsTabOrderContract
  AddonQuickSettingsShellContract

Device / Profile
  QuickSettingsPageSnapshot
  QuickSettingsPresentation
  QuickSettingsMutationAdapter

Setting
  AddonQuickSettingsTabOrderSnapshot
  AddonQuickSettingsTabOrderMoveIntent
  AddonQuickSettingsTabOrderMutationResult
  AddonQuickSettingsTabOrderProduct

Shortcut
  AddonQuickSettingsShortcutSlotId
  AddonQuickSettingsShortcutContract
~~~

PR5 is not another feature migration.

It must:

1. remove only real remaining product-policy duplication;
2. freeze final QAM/Overlay parity with regression tests;
3. retain legitimate renderer ABI/lifecycle constants that are not product authority;
4. update living architecture documents to the implemented state;
5. perform final real-device parity acceptance.

Do not manufacture production churn merely because this is the final PR.

---

## 2. Required reading before editing

Read:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md

docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR1_FOUNDATION_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR2_QAM_FIVE_TAB_NATIVE_SHELL_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR3_SETTING_TAB_ORDER_PARITY_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR4_SHORTCUT_PARITY_WORK_ORDER.md
~~~

Then inspect current source:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsTabOrderContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShortcutContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs

src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw.QamHost/Program.cs

src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs
src/SteamInputAddonforClaw.Overlay/OverlayQuickSettingsPageBinding.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
~~~

---

## 3. Current production facts at 8c333b1

### Shell

The canonical five identities, order validation, and labels already live in shared Contracts.

Overlay no longer has OverlayTabId.

QAM has numeric AQS_TAB_* constants only as renderer-side enum/ABI discriminants. It does not own a five-label product array.

### Device / Profile

Both renderers already consume QuickSettingsPageSnapshot.

Labels, section/row order, ranges, options, commit policy, grouping, and linked constraints come from shared Runtime-side projection.

The old feature-specific QAM Device/Profile bridge methods are already gone.

### Setting

Both surfaces already use the same typed tab-order state and TabId + Delta mutation meaning.

Runtime computes the authoritative new order.

### Shortcut

PR #530 promoted the four slot identities/labels/status into AddonQuickSettingsShortcutContract.

Overlay keeps local 2x2 navigation only.

QAM renders the same shared static product read-only.

### Controller

Controller intentionally remains a shell identity with no real page contract.

Keep it that way in PR5.

Do not invent vibration/LED/mapping product schemas.

---

## 4. One clear remaining product-policy duplicate

Current qam.js still contains:

~~~javascript
const QS_FALLBACK_COMMIT_DELAY_MS = 2000;
~~~

and the generic scheduler still falls back to that local value when the shared row delay is absent/invalid.

This is now a second product-policy authority.

The shared row already owns:

~~~text
QuickSettingsCommitPolicy
  mode
  delayMilliseconds
~~~

Overlay already requires a valid positive shared delay.

QAM should do the same.

---

## 5. Remove QAM local debounce fallback

Delete QS_FALLBACK_COMMIT_DELAY_MS.

Do not give scheduleQamSliderCommit a local default delay.

Preferred caller validation:

~~~javascript
const delayMs =
  row.commitPolicy?.mode === QS_COMMIT_TRAILING
    ? Number(row.commitPolicy.delayMilliseconds)
    : NaN;

if (!Number.isFinite(delayMs) || delayMs <= 0) {
  setError("Quick Settings is unavailable.");
  return;
}
~~~

Then pass delayMs explicitly to the scheduler.

The scheduler timer must use the supplied value directly.

Do not silently turn malformed/missing shared policy into 2000 ms.

This is the main production cleanup expected in PR5.

---

## 6. Keep legitimate renderer ABI identities

Do not remove the narrow numeric JS constants that decode closed C# enum values:

~~~text
QS_PAGE_DEVICE / PROFILE
QS_CONTROL_TOGGLE / SLIDER
QS_SLIDER_NUMERIC / DISCRETE
QS_COMMIT_TRAILING
QS_VALUE_BOOLEAN / INTEGER

AQS_TAB_DEVICE / PROFILE / CONTROLLER / SHORTCUT / SETTING

AQS_SHORTCUT_SLOT_1 / 2 / 3 / 4
~~~

They do not own user-facing labels/order/options/ranges/debounce.

Do not replace them with strings, reflection, registries, or a generic schema interpreter.

---

## 7. Keep bounded historical QAM descriptor cleanup unless proven unreachable

Current qam.js recognizes historical Addon descriptor markers:

~~~text
LEGACY_ADDON_DEVICE_TAB_KEY
LEGACY_ADDON_PROFILE_TAB_KEY
marker === true
~~~

These are cleanup-only lifecycle identities, not current product tabs.

Supported QAM lifecycle includes restart, crash/reacquisition, document reload, and uninstall cleanup.

Do not delete these constants solely because PR5 is named cleanup.

Remove them only if current code/tests prove historical descriptors cannot survive into a valid new install path.

Otherwise retain them and clarify in comments/tests:

~~~text
legacy descriptor cleanup identity
!=
current product authority
~~~

Do not add any additional compatibility manager.

---

## 8. Keep current real bridge paths

Keep:

~~~text
captureStatus
captureQuickSettingsShell
captureQuickSettingsTabOrder
captureQuickSettingsShortcut
captureQuickSettingsPage
mutateQuickSetting
moveQuickSettingsTab
~~~

captureQuickSettingsShortcut is a real current path for the shared static Shortcut product.

captureQuickSettingsShell and captureQuickSettingsTabOrder have different responsibilities:

~~~text
shell
-> five-tab composition

tab-order snapshot
-> Setting rows + move boundaries
~~~

PR5 is not a transport-consolidation PR.

---

## 9. Protocol versions remain unchanged

Current and expected after PR5:

~~~text
FrontendTransportProtocol = 32
OverlayTransportProtocol  = 8
~~~

If a real wire shape must change, stop and reassess scope rather than silently bumping protocol in a cleanup PR.

---

## 10. Add one final parity regression suite

Preferred new test file:

~~~text
tests/SteamInputAddonforClaw.Tests/AddonQuickSettingsSurfaceParityTests.cs
~~~

Do not build a generic renderer-test framework.

This suite should be a compact architecture guardrail.

---

## 11. Shell parity guard

Use a non-default order, for example:

~~~text
Setting
Shortcut
Device
Controller
Profile
~~~

Build AddonQuickSettingsShellContract.Create(order).

Assert:

- exactly five known identities;
- exact supplied order;
- labels from AddonQuickSettingsShellContract.LabelFor;
- no duplicate/missing identity.

Source guards:

### QAM

~~~text
captureQuickSettingsShell
-> validateQuickSettingsShell
-> shellTabs.map
-> title: tab.label
~~~

No local canonical five-label/order product table.

### Overlay

~~~text
OverlayTabState
-> AddonQuickSettingsTabOrderContract

OverlayWindow
-> AddonQuickSettingsShellContract.LabelFor
~~~

No OverlayTabId and no local five-label switch.

Selected-tab policy remains surface-specific.

---

## 12. Device/Profile parity guard

Use one representative QuickSettingsPageSnapshot shape containing:

- Toggle;
- Numeric Slider;
- Discrete Slider;
- ordered options;
- TrailingDebounce;
- commit group;
- linked constraint.

Do not duplicate every feature test.

Prove ownership:

### QAM

Renderer reads product metadata from:

~~~text
row.label
row.controlKind
row.sliderSpec
row.commitPolicy
row.commitGroupId
section/row payload order
~~~

and does not own:

- CPU Boost option tables;
- Power Mode option tables;
- Device/Profile row-label tables;
- a local production debounce duration;
- feature-specific mutation method names.

### Overlay

OverlayQuickSettingsPageBinding uses the same typed metadata and policy.DelayMilliseconds.

No Overlay-only production debounce constant.

---

## 13. Setting parity guard

Build AddonQuickSettingsTabOrderProduct.Create(customOrder).

Assert:

- same five identities;
- shared labels;
- correct CanMoveEarlier;
- correct CanMoveLater.

Source guards:

### QAM

~~~text
moveQuickSettingsTab
-> result.state
-> authoritative state applied
-> shellTabs updated from authoritative rows
~~~

### Overlay

~~~text
TabOrderMoveRequested
-> AddonQuickSettingsTabOrderMoveIntent
-> authoritative mutation result
-> ApplyTabOrderState
~~~

No renderer constructs/persists a complete order independently.

---

## 14. Shortcut parity guard

Build AddonQuickSettingsShortcutContract.Create().

Assert exactly:

~~~text
Slot1 / Slot 1 / Unassigned
Slot2 / Slot 2 / Unassigned
Slot3 / Slot 3 / Unassigned
Slot4 / Slot 4 / Unassigned
~~~

QAM guards:

- captureQuickSettingsShortcut;
- payload-order slots.map;
- labels/status from payload;
- no local Slot 1-4 label table;
- no local Unassigned product value;
- no Shortcut mutation/action.

Overlay guards:

- AddonQuickSettingsShortcutContract.Create();
- labels/status from shared slot;
- geometry remains renderer-local through index / 2 and index % 2;
- OverlayShortcutSelection remains transient local navigation only.

Do not force QAM to mimic Overlay's 2x2 selected-tile model while slots are read-only.

---

## 15. Controller placeholder guard

Assert:

- AddonQuickSettingsTabId.Controller exists;
- QuickSettingsPageId.Controller does not exist;
- QAM maps Controller to placeholder content;
- Overlay maps Controller to its placeholder path;
- no Controller mutation RPC/schema exists.

Exact placeholder wording may remain renderer-specific.

Do not add a shared placeholder manager/string DTO solely for text parity.

---

## 16. Update QAM debounce tests

Current QamFrontendContractTests explicitly expects QS_FALLBACK_COMMIT_DELAY_MS = 2000.

Replace that expectation.

New assertions:

~~~text
QS_FALLBACK_COMMIT_DELAY_MS absent
QAM_SLIDER_COMMIT_DELAY_MS absent
PROFILE_SLIDER_COMMIT_DELAY_MS absent

delay source
-> row.commitPolicy.delayMilliseconds

invalid / missing / non-positive delay
-> no delayed mutation scheduled
~~~

Keep existing pending-draft correctness coverage:

- one generic scheduler;
- latest token wins;
- ordinary invalidation preserves valid pending drafts;
- stale Profile context guard;
- authoritative result wins;
- uninstall retires pending bridge consumers.

---

## 17. Preserve QAM generation/lifecycle safety

Do not regress:

- exactly one current Addon descriptor;
- state.addonTabDescriptor generation ownership;
- native component fail-closed resolution;
- current Tabs wrapper export handling;
- verified outer visibility open/close seam;
- fresh-open selection;
- descriptor cleanup on uninstall;
- pending request rejection on stop;
- bounded GamepadUI reacquisition;
- no polling.

Cleanup must not weaken realistic restart/reload/crash behavior.

---

## 18. Preserve Overlay lifecycle

Do not change:

- OQ4 capture/neutral/release;
- Ready/Visible admission;
- Device/Profile generic binding;
- Setting authoritative state;
- Shortcut local 2x2 selection;
- Show reset policy;
- B/LB/RB navigation;
- no polling.

No reason exists in PR5 to touch controller ownership or presentation.

---

## 19. Expected production footprint

Expected production behavior change:

~~~text
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
~~~

No normal reason to modify Runtime, transport, Overlay, Contracts, or controller lifecycle production code unless a final parity test exposes a real duplicate.

A tests/docs-heavy final PR is acceptable.

---

## 20. Living architecture docs must be updated

Historical work orders remain historical.

Update only documents that claim current architecture/roadmap.

### ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md

Update status from proposed to implemented.

Record the sequence:

~~~text
#527 shared shell foundation
#528 QAM five-tab shell
#529 shared Setting parity
#530 shared Shortcut parity
PR5 convergence cleanup / acceptance
~~~

Refresh stale current-state statements that still mention:

- OverlayTabId;
- QAM as Device/Profile-only;
- Setting/Shortcut as future work.

Add a concise final-state ownership section.

Do not erase the design rationale or five-PR history.

### SHARED_FRONTEND_ARCHITECTURE_V2.md

This is labeled current design authority but still describes the old PR #498 baseline and:

~~~text
Frontend 27
Overlay 6
~~~

Update current-state sections to:

~~~text
Frontend 32
Overlay 8
~~~

and current shared product topology.

Rewrite stale "current duplication" wording that no longer matches production.

Do not rewrite still-valid historical rationale.

### SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md

It is still labeled current roadmap at an old baseline.

Minimal update:

- mark the original SF-V2 roadmap completed;
- record current protocol 32 / 8;
- point to the 2026-09-18 shared-surface architecture for the later shell/Setting/Shortcut convergence;
- retain historical phase sections as implementation record.

Do not mass-edit old focused work orders.

---

## 21. Historical document policy

Do not update historical work orders simply because their baseline versions are old.

Examples:

~~~text
SF_V2_01...
SF_V2_02...
OQ5_UI...
PR1/PR2/PR3/PR4 shared-surface work orders
~~~

They describe the implementation contract at their time.

Only living docs that explicitly claim current authority/roadmap should be refreshed.

---

## 22. Real-device final acceptance - QAM

Verify:

### Shell

- one Addon top-level tab;
- five native inner tabs;
- order matches Runtime;
- labels match shared shell;
- no duplicate historical Addon tabs.

### Device/Profile

- same product controls as Overlay;
- shared labels/options/ranges;
- slider debounce unchanged in normal use;
- delay comes from shared policy only;
- malformed delay does not silently become 2000 ms;
- authoritative result still wins;
- Profile AppId stale guard still works.

### Setting

- five rows;
- one-position moves;
- boundary behavior;
- authoritative reorder;
- selected inner tab identity preserved.

### Shortcut

- four shared slots;
- shared labels/status;
- no action.

### Controller

- remains placeholder.

### Lifecycle

- close/reopen;
- GamepadUI document reload;
- QamHost teardown;
- no duplicate tab;
- no polling.

---

## 23. Real-device final acceptance - Overlay

Verify:

- same shell identities/order/labels as QAM;
- Device/Profile product controls match QAM;
- Setting semantics match QAM;
- Shortcut product meaning matches QAM;
- 2x2 Shortcut navigation remains bounded;
- pointer/touch remains;
- A on Unassigned remains no-op;
- Controller remains placeholder;
- OQ4 capture/release unchanged;
- B dismiss unchanged;
- no input leak;
- no presentation change caused by UI visibility.

---

## 24. Cross-surface final acceptance

Use a non-default tab order, for example:

~~~text
Setting
Shortcut
Device
Controller
Profile
~~~

Verify both surfaces show that exact order and labels.

Then verify:

~~~text
Device snapshot
-> same product controls

Profile snapshot for one active game
-> same product controls

Setting move in QAM
-> Overlay converges to same persisted order

Setting move in Overlay
-> QAM converges to same persisted order

Shortcut
-> same Slot1-Slot4 / Unassigned product meaning
~~~

Pixel layout may differ.

---

## 25. Protocol and persistence non-goals

Keep:

~~~text
Frontend protocol = 32
Overlay protocol = 8
settings JSON key = OverlayTabOrder
~~~

Do not rename the persisted historical key.

Do not add migration machinery.

Do not add new Shortcut/Controller persistence.

---

## 26. Full1902 invariants - untouched

PR5 must not modify:

- Center M authority;
- PID1901/PID1902 policy;
- reboot-bound transitions;
- DirectInput ownership;
- HidHide;
- VIIPER;
- X360/SteamDeck presentation;
- Steam/BPM presentation switching;
- PnP recovery;
- Sleep/Hibernate/Resume;
- Restart/Shutdown;
- fail-close behavior;
- OQ4 capture/release;
- WING/OEM1 routing.

---

## 27. Overengineering guardrails

Do not add:

- generic page/provider registry;
- plugin UI framework;
- shared renderer interface;
- React/XAML schema engine;
- Controller placeholder manager;
- cross-surface selected-tab service;
- Shortcut action registry;
- generic compatibility manager;
- new frontend manager/service;
- epochs/barriers;
- polling;
- retry schedulers solely for parity.

The final architecture is already simple:

~~~text
one product authority
+
two renderer/admission hosts
~~~

---

## 28. Validation

Run:

~~~powershell
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx --no-restore -v:minimal
dotnet test SteamInputAddonforClaw.slnx --no-restore --logger "console;verbosity=minimal"
node --check src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
git diff --check
~~~

Focused iteration:

~~~powershell
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj --no-restore --filter "FullyQualifiedName~AddonQuickSettingsSurfaceParity|FullyQualifiedName~QamFrontendContract|FullyQualifiedName~OverlayTabOrder|FullyQualifiedName~Shortcut"
~~~

Focused tests do not replace the full suite.

---

## 29. Definition of done

- [ ] Baseline is PR #530 / 8c333b1... or later rebased main.
- [ ] QAM local 2000 ms fallback debounce is removed.
- [ ] Slider delay comes only from shared row.commitPolicy.delayMilliseconds.
- [ ] Invalid/missing delay fails closed instead of inventing a local default.
- [ ] Numeric JS ABI discriminants remain narrow and typed.
- [ ] Shell has one product authority.
- [ ] Device/Profile have one product authority.
- [ ] Setting has one state/mutation authority.
- [ ] Shortcut has one identity/label/status authority.
- [ ] Controller remains a shell placeholder with no fake schema.
- [ ] Historical QAM descriptor cleanup is retained unless proven safely unreachable.
- [ ] No old feature-specific QAM Device/Profile bridge path returns.
- [ ] Current Shortcut bridge path remains.
- [ ] Frontend protocol remains 32.
- [ ] Overlay protocol remains 8.
- [ ] Final parity regression suite exists.
- [ ] Living shared-frontend docs describe implemented state.
- [ ] Historical work orders remain historical.
- [ ] Full1902/OQ4 behavior is untouched.
- [ ] Full build/test suite passes.
- [ ] Real QAM acceptance passes.
- [ ] Real Overlay acceptance passes.
- [ ] Cross-surface parity acceptance passes.

---

## 30. Review standard

Blocking examples:

- QAM still owns a production Device/Profile debounce value;
- same Runtime state produces different tab labels/order across surfaces;
- one renderer keeps a second Device/Profile option/range/row table;
- Setting mutation semantics diverge;
- Shortcut labels/status diverge;
- Controller gets speculative schema;
- cleanup removes realistic QAM restart/reload/uninstall safety;
- protocol changes without an intentional version decision;
- cleanup touches Full1902/OQ4 ownership.

Do not block for:

- numeric renderer ABI constants;
- renderer-specific placeholder wording;
- Steam native vs WinUI visual differences;
- historical work-order baseline text;
- theoretical instruction-level races;
- unsupported multi-session scenarios;
- future Controller/Shortcut features;
- desire for a more generic framework.

Final target:

> one product authority, two renderer/admission hosts, no duplicate product policy.