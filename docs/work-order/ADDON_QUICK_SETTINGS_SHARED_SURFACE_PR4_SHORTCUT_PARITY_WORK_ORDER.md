# Work Order — Addon Quick Settings Shared Surface PR4: Shared Shortcut Composition + QAM/Overlay Parity

> **Historical / obsolete:** This document's fixed four-slot QAM/Overlay parity design predates the Shortcut Foundation. QAM is retired and the Overlay now consumes the Runtime-owned dynamic Shortcut dashboard over Overlay protocol v12. Do not implement or revive this work order; see `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md` and the Shortcut Foundation PR-E implementation.

> **Date:** 2026-09-19  
> **Status:** Ready for implementation  
> **Reviewed production baseline:** `main` at `9aa2d52aefd534698110b225472e9d63116afba6` after PR #529  
> **Previous shared-surface PRs:** PR #527, PR #528, PR #529  
> **Architecture authority:** `docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md`  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and referenced Full1902 documents  
> **Product scope:** Standalone Full PID1902  
> **CTW integration:** Out of scope  
> **Main goal:** Give QAM and Overlay one shared, closed four-slot Shortcut product definition without inventing assignment, execution, persistence, or a generic tile/action framework.

---

## 1. Goal

PR1 established one shared five-tab identity/order authority.

PR2 changed QAM to:

```text
Steam QAM
└─ Addon
   └─ native inner Tabs
      ├─ Device
      ├─ Profile
      ├─ Controller
      ├─ Shortcut
      └─ Setting
```

PR3 made Setting a real shared product with one Runtime-owned tab-order state/mutation meaning.

At the reviewed PR #529 baseline, Shortcut is still asymmetric:

```text
Overlay Shortcut
→ functional visual/navigation shell
→ fixed 2x2 layout
→ Slot 1 / Slot 2 / Slot 3 / Slot 4
→ all Unassigned
→ no action execution

QAM Shortcut
→ placeholder only
```

PR4 must converge that into:

```text
        shared static Shortcut product definition
             four known slot identities
             four shared labels
             shared Unassigned status
                        │
          ┌─────────────┴─────────────┐
          ▼                           ▼
      Steam QAM                    Overlay
 native read-only rows          existing 2x2 shell
```

This PR does **not** implement shortcut assignment or execution.

The current product has no real dynamic Shortcut state. Therefore do not create fake Runtime state, fake persistence, or fake mutation RPCs merely to make the architecture look symmetrical.

---

## 2. Required reading before editing

Read these first:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md
docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md

docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR1_FOUNDATION_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR2_QAM_FIVE_TAB_NATIVE_SHELL_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR3_SETTING_TAB_ORDER_PARITY_WORK_ORDER.md

docs/overlayui/OQ5_UI_11_SHORTCUT_2X2_SLOT_SHELL_WORK_ORDER.md
```

Then inspect the current production source:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsTabOrderContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs

src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js

src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs

tests/SteamInputAddonforClaw.Tests/OverlayShortcutSelectionTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayDeviceQuickSettingsTransportTests.cs
```

Do not use CTW integration as an implementation target.

---

## 3. Current production facts at `9aa2d52...`

### 3.1 Overlay already has the entire current Shortcut product

Current `OverlayWindow.BuildShortcutPage()` owns this local array:

```csharp
var slots = new (OverlayShortcutSlotId Id, string Label, int Row, int Column)[]
{
    (OverlayShortcutSlotId.Slot1, "Slot 1", 0, 0),
    (OverlayShortcutSlotId.Slot2, "Slot 2", 0, 1),
    (OverlayShortcutSlotId.Slot3, "Slot 3", 1, 0),
    (OverlayShortcutSlotId.Slot4, "Slot 4", 1, 1),
};
```

and separately hard-codes:

```csharp
Text = "Unassigned"
```

That means the product identity/labels/status are still Overlay-owned.

PR4 removes that product duplication.

### 3.2 The four-slot identity is intentionally still Overlay-local

Current `OverlayShortcutSelection.cs` contains:

```csharp
internal enum OverlayShortcutSlotId
{
    Slot1,
    Slot2,
    Slot3,
    Slot4,
}
```

The original OQ5-UI-11 work order explicitly said to keep it local until a real cross-surface/persisted requirement existed.

PR4 is now that requirement.

Promote this identity once. Do not create a second parallel enum.

### 3.3 The current Shortcut product has no assignment/action contract

Current production source has no:

```text
ShortcutAction
ShortcutCommand
ShortcutAssignment
Shortcut persistence setting
Shortcut execution RPC
Shortcut action catalog
```

Every slot is deliberately:

```text
Unassigned
```

and Overlay Accept currently performs no product action.

Do not infer future actions from front-button mapping, Main UI features, launch commands, or other unrelated features.

### 3.4 QAM still renders Shortcut as a placeholder

Current `buildInnerTabContent(...)` groups:

```text
Controller
Shortcut
```

into:

```text
"This page is not available in QAM yet."
```

PR4 replaces only the Shortcut branch.

Controller remains a placeholder.

### 3.5 QAM currently resolves only the native controls already needed

The current native resolver returns:

```text
SliderField
ToggleField
PanelSection
PanelSectionRow
Tabs
```

It does **not** resolve:

```text
ButtonItem
DialogButton
generic focusable tile
native grid/card action control
```

Do not add another Steam-version-sensitive native-component discovery path solely to make four non-functional Unassigned slots look like clickable buttons.

The architecture requires product parity, not pixel-identical layout.

### 3.6 Current protocol baselines after PR3

Current production versions are:

```text
FrontendTransportProtocol = 32
OverlayTransportProtocol  = 8
```

PR4 should not bump either version unless implementation unexpectedly changes a real existing wire contract.

The preferred design below requires no protocol bump.

---

## 4. Product decision

Use one small shared **static Shortcut product contract**.

Because the current Shortcut product has no mutable Runtime truth, the shared product definition itself is the authority.

Target ownership:

```text
Contracts
└─ AddonQuickSettingsShortcutContract
   ├─ Slot1
   ├─ Slot2
   ├─ Slot3
   ├─ Slot4
   ├─ shared labels
   └─ shared "Unassigned" status
          │
          ├─ Overlay consumes directly in C#
          │
          └─ QamFrontendBridge serializes the same contract to qam.js
```

This is intentionally **not**:

```text
Runtime settings
→ persistence
→ mutation API
→ Overlay transport
→ QAM transport
```

because there is no real Shortcut state to persist or mutate yet.

When real shortcut assignment/execution exists, add the smallest real typed Runtime contract then.

---

## 5. Add the shared Shortcut contract

Preferred new source:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShortcutContracts.cs
```

### 5.1 Shared slot identity

Promote the existing Overlay-local identity to:

```csharp
public enum AddonQuickSettingsShortcutSlotId
{
    Slot1,
    Slot2,
    Slot3,
    Slot4,
}
```

Numeric order is part of the current ABI once qam.js mirrors these values.

Do not add arbitrary string IDs.

### 5.2 Shared slot projection

Use a small closed row/slot shape, conceptually:

```csharp
public sealed record AddonQuickSettingsShortcutSlot(
    AddonQuickSettingsShortcutSlotId SlotId,
    string Label,
    string StatusLabel);

public sealed record AddonQuickSettingsShortcutSnapshot(
    bool Available,
    IReadOnlyList<AddonQuickSettingsShortcutSlot> Slots)
{
    public static AddonQuickSettingsShortcutSnapshot Unavailable() =>
        new(false, Array.Empty<AddonQuickSettingsShortcutSlot>());
}
```

The current available state contains exactly:

```text
Slot1 / "Slot 1" / "Unassigned"
Slot2 / "Slot 2" / "Unassigned"
Slot3 / "Slot 3" / "Unassigned"
Slot4 / "Slot 4" / "Unassigned"
```

### 5.3 One static product factory

Add one small closed product authority, conceptually:

```csharp
public static class AddonQuickSettingsShortcutContract
{
    public static AddonQuickSettingsShortcutSnapshot Create() =>
        new(
            true,
            [
                new(AddonQuickSettingsShortcutSlotId.Slot1, "Slot 1", "Unassigned"),
                new(AddonQuickSettingsShortcutSlotId.Slot2, "Slot 2", "Unassigned"),
                new(AddonQuickSettingsShortcutSlotId.Slot3, "Slot 3", "Unassigned"),
                new(AddonQuickSettingsShortcutSlotId.Slot4, "Slot 4", "Unassigned"),
            ]);
}
```

Exact property/factory names may differ slightly, but keep the contract this small.

### 5.4 What must NOT be added

Do not add:

```text
ActionId
ActionType
CommandName
ExecutablePath
Arguments
Hotkey
Launch target
Icon URI
Tile color
IsExecuting
CanExecute
assignment mutation
execution mutation
generic payload dictionary
arbitrary action string
```

There is no current product authority for any of those.

---

## 6. Keep layout geometry surface-specific

The shared product owns:

```text
slot identity
slot order
slot label
current placeholder/status text
```

The shared product does **not** own:

```text
row
column
pixel size
WinUI Border
Steam React component
focus state
selected tile
```

### Overlay

The existing visual geometry remains:

```text
Slot1 Slot2
Slot3 Slot4
```

Map shared snapshot order to the current 2x2 renderer:

```csharp
row    = index / 2;
column = index % 2;
```

Do not put Row/Column into the shared contract merely because Overlay currently uses a 2x2 Grid.

### QAM

QAM may render the same four slots as four ordered Steam-native rows.

It does **not** need to imitate the WinUI 2x2 pixels.

This is consistent with the architecture:

```text
same product
different renderer
```

---

## 7. Overlay migration

### 7.1 Remove the local slot enum

Delete:

```csharp
internal enum OverlayShortcutSlotId
```

from `OverlayShortcutSelection.cs`.

Update `OverlayShortcutSelection` to use:

```csharp
AddonQuickSettingsShortcutSlotId
```

Keep the selection object Overlay-local.

Its job is still only transient 2D selection state.

Do not move:

```text
SelectedSlot
MoveUp
MoveDown
MoveLeft
MoveRight
Reset
```

into the shared product contract.

Navigation is renderer-local state.

### 7.2 Render the existing 2x2 page from the shared product

Current `BuildShortcutPage()` must stop defining its own:

```text
Slot1–Slot4 tuple array
"Slot 1" ... "Slot 4"
"Unassigned"
```

Instead:

```csharp
var shortcut = AddonQuickSettingsShortcutContract.Create();
```

and iterate `shortcut.Slots` in shared order.

The existing:

```text
Grid
Border tiles
pointer/touch selection
logical selected-border visual
2x2 controller geometry
```

remains Overlay-owned and unchanged.

### 7.3 Keep one selection authority

Continue using:

```text
OverlayShortcutSelection
```

for the visible Shortcut page.

Do not register the four tiles into the linear `OverlayRowSelection` model.

Do not introduce a generic 2D navigation graph.

### 7.4 Preserve current interaction

Required unchanged behavior:

```text
enter Shortcut tab → Slot1 selected
Left/Right/Up/Down → bounded 2x2 movement
pointer tap → selects the tapped slot
A on any slot → no product action
B → existing Overlay dismissal path
LB/RB → existing top-level tab navigation
```

---

## 8. QAM bridge: expose the shared static product locally

Add one narrow bridge method:

```text
captureQuickSettingsShortcut
```

Preferred implementation in `QamFrontendBridge.HandleRequestAsync(...)`:

```csharp
"captureQuickSettingsShortcut" =>
    AddonQuickSettingsShortcutContract.Create(),
```

This is deliberately bridge-local.

Do **not** route this through:

```text
IAddonFrontendControl
NamedPipeAddonFrontendClient
NamedPipeAddonFrontendServer
FrontendRpcMethod
```

because there is no dynamic Runtime state.

### Why this is correct now

The source of truth is the same compiled Contracts assembly for both surfaces:

```text
Overlay C# renderer
→ AddonQuickSettingsShortcutContract.Create()

QAM C# bridge
→ AddonQuickSettingsShortcutContract.Create()
→ JSON
→ qam.js
```

That gives one product definition without inventing a new Runtime feature seam.

### Future rule

When real shortcut assignment/execution is introduced:

```text
static shared product
→ extend/replace with real typed Runtime state/mutation
```

at that time.

Do not pre-build that future transport in PR4.

---

## 9. QAM renderer

### 9.1 Replace only the Shortcut placeholder

Current:

```text
Controller + Shortcut
→ one placeholder path
```

Change to:

```text
Controller
→ existing placeholder

Shortcut
→ ShortcutPanel
```

Do not modify Device/Profile/Setting renderer behavior beyond mechanical call-site adjustments.

### 9.2 Add only ABI slot identities in JS

qam.js may mirror the closed C# enum numerically:

```js
const AQS_SHORTCUT_SLOT_1 = 0;
const AQS_SHORTCUT_SLOT_2 = 1;
const AQS_SHORTCUT_SLOT_3 = 2;
const AQS_SHORTCUT_SLOT_4 = 3;

const KNOWN_AQS_SHORTCUT_SLOT_IDS = new Set([
  AQS_SHORTCUT_SLOT_1,
  AQS_SHORTCUT_SLOT_2,
  AQS_SHORTCUT_SLOT_3,
  AQS_SHORTCUT_SLOT_4,
]);
```

These are ABI identities only.

qam.js must **not** own:

```text
"Slot 1"
"Slot 2"
"Slot 3"
"Slot 4"
"Unassigned"
canonical four-slot order
```

### 9.3 Validate the bridge payload narrowly

Add a small validator:

```text
available === true
slots is array
slots.length === 4
all SlotId values known
no duplicate SlotId
label non-empty
statusLabel non-empty
```

If invalid:

```text
Shortcut fails closed locally
Device/Profile/Setting remain usable
```

Do not fabricate a local default four-slot array in JS.

### 9.4 Capture Shortcut independently

Do not make Shortcut failure fail the entire Addon inner shell.

Preferred shape:

```text
ShortcutPanel mounts
→ request("captureQuickSettingsShortcut")
→ validate
→ render or local unavailable state
```

Do not add Shortcut to the existing shell/order Promise.all in a way that would make one bridge-local Shortcut failure blank the complete Addon panel.

No polling.

No StateInvalidated subscription is required for the current static product.

### 9.5 Use existing Steam-native layout primitives

Render the four slots in payload order using the already-resolved:

```text
PanelSection
PanelSectionRow
```

A simple read-only row representation is sufficient, for example:

```text
Slot 1    Unassigned
Slot 2    Unassigned
Slot 3    Unassigned
Slot 4    Unassigned
```

The exact text child structure may use simple React text elements inside `PanelSectionRow`.

Do not add:

```text
ButtonItem resolver
DialogButton resolver
custom HTML button
custom CSS tile grid
fake ToggleField
fake SliderField
onClick action
keyboard shortcut handler
```

solely for four currently non-functional slots.

### 9.6 No QAM Shortcut selection authority in PR4

Because all four slots are read-only/unassigned:

```text
no slot activation
no slot mutation
no slot selected-state persistence
no QAM-local 2D navigation model
```

Steam native tab navigation remains sufficient.

When slots become actionable, add the smallest native interaction needed by that real product in the later focused feature PR.

---

## 10. Protocol/version policy

Expected after PR4:

```text
FrontendTransportProtocol = 32
OverlayTransportProtocol  = 8
```

No bump expected.

Reason:

- no `.Frontend` RPC is added;
- no existing `.Frontend` payload changes;
- no `.Overlay` frame changes;
- QAM bridge gains only one local static product method;
- Overlay consumes the shared Contracts definition in-process.

Do not bump versions merely because a C# contract type is added to the Contracts assembly.

If implementation discovers that a real wire change is unavoidable, stop and make that explicit rather than silently changing a current frame.

---

## 11. No persistence in PR4

Do not modify:

```text
AppSettings
SettingsStore
StartupSettingsCoordinator
OverlayTabOrder persisted key
ProfileStore
registry
```

No new JSON property such as:

```text
ShortcutSlots
ShortcutAssignments
ShortcutActions
```

is justified.

The existing historical `"OverlayTabOrder"` key remains untouched.

---

## 12. No mutation/execution contract in PR4

Do not add any method named conceptually like:

```text
SetShortcutSlot
AssignShortcut
ExecuteShortcut
InvokeShortcut
MutateShortcut
LaunchShortcut
```

Do not add a mutation result type.

A on Overlay remains a deliberate no-op.

QAM rows remain read-only.

This is a product decision, not a missing implementation.

---

## 13. Expected production footprint

Likely production changes:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShortcutContracts.cs   new
src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
src/SteamInputAddonforClaw.Overlay/OverlayShortcutSelection.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
```

No normal production changes expected in:

```text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw/Settings/*
```

If implementation starts expanding into those areas, re-check whether real current Shortcut state actually requires it.

---

## 14. Contract tests

Add focused tests, preferably:

```text
tests/SteamInputAddonforClaw.Tests/AddonQuickSettingsShortcutContractTests.cs
```

Required:

### 14.1 Exact four-slot product

Assert exactly:

```text
Slot1
Slot2
Slot3
Slot4
```

once each and in canonical order.

### 14.2 Shared labels/status

Assert exactly:

```text
Slot 1 / Unassigned
Slot 2 / Unassigned
Slot 3 / Unassigned
Slot 4 / Unassigned
```

in the shared contract.

These values should not be re-asserted as independent product constants in both renderer tests.

### 14.3 Unavailable snapshot

Verify:

```text
Available = false
Slots = empty
```

for the fail-closed helper if that shape is used.

---

## 15. Overlay tests

Update:

```text
OverlayShortcutSelectionTests.cs
```

to use:

```text
AddonQuickSettingsShortcutSlotId
```

instead of the deleted Overlay-local enum.

Keep all existing geometry tests green:

```text
Slot1 Right → Slot2
Slot1 Down  → Slot3
Slot2 Left  → Slot1
Slot2 Down  → Slot4
Slot3 Up    → Slot1
Slot3 Right → Slot4
Slot4 Up    → Slot2
Slot4 Left  → Slot3
```

and all bounded edge no-ops.

Add/adjust a source/contract test to prove:

- Overlay no longer declares `OverlayShortcutSlotId`;
- `BuildShortcutPage()` consumes `AddonQuickSettingsShortcutContract`;
- local canonical `"Slot 1"..."Slot 4"` product array is gone;
- local `"Unassigned"` product constant is gone;
- 2x2 selection remains Overlay-local.

Do not add WinUI automation solely for trivial text binding.

---

## 16. QAM bridge tests

Extend `QamFrontendBridgeTests.cs`.

Required:

```text
captureQuickSettingsShortcut
→ succeeds
→ returns the shared four-slot snapshot
```

Prefer a test that proves the method is bridge-local and does not require a new frontend RPC.

Do not add a fake mutation method just to test a failure path.

Malformed/unsupported bridge methods must continue failing through the existing generic bridge error behavior.

---

## 17. QAM source/contract tests

Extend `QamFrontendContractTests.cs`.

Assert:

- Shortcut no longer shares the Controller placeholder branch;
- qam.js calls `captureQuickSettingsShortcut`;
- qam.js validates exactly four known slot identities;
- qam.js maps returned `slots` in payload order;
- labels/status come from payload;
- qam.js does not contain a local canonical four-slot label array;
- qam.js does not hard-code `"Unassigned"` as the Shortcut product source;
- no Shortcut mutation/execution request exists;
- no new `ButtonItem` / `DialogButton` resolver is introduced;
- no custom HTML button/grid control is introduced;
- Controller remains placeholder;
- Device/Profile generic renderers remain unchanged;
- Setting tab-order renderer remains unchanged;
- no polling is introduced.

Do not write brittle whitespace-only source tests.

Test stable product/architecture invariants.

---

## 18. Existing transport tests

Keep current protocol assertions:

```text
Frontend = 32
Overlay  = 8
```

Current v32/v8 handshake regression coverage must stay green.

Do not rewrite unrelated transport tests because PR4 does not change those wires.

---

## 19. Failure behavior

### QAM

If the local bridge request fails or the payload is malformed:

```text
Shortcut tab
→ local unavailable/read-only message
```

while:

```text
Device
Profile
Setting
Controller placeholder
QAM host
Runtime
```

remain unaffected.

Do not fail the whole Addon tab because Shortcut static state could not be rendered.

### Overlay

The shared static contract should always produce a valid four-slot product.

If a renderer exception occurs, existing Overlay process/session failure handling remains authoritative.

Do not add a Shortcut-specific recovery manager.

---

## 20. Full1902 / lifecycle non-goals

PR4 must not modify:

```text
Center M authority
PID1901/PID1902 ownership
DirectInput
HidHide
VIIPER
X360/SteamDeck presentation
Steam/BPM presentation switching
PnP recovery
Sleep/Hibernate/Resume
Restart/Shutdown
OQ4 capture/release
front-button routing
```

Shortcut UI state must never become controller authority.

---

## 21. Overengineering guardrails

Do not add:

```text
ShortcutManager
ShortcutProvider
ShortcutRegistry
ActionRegistry
TileProvider
plugin model
generic dashboard/card framework
generic 2D navigation graph
reflection-based action discovery
arbitrary command strings
JSON action schema
cross-surface selected-slot synchronization
slot selection persistence
polling
epoch/barrier/revision system
```

The current product is four fixed read-only Unassigned slots.

Model exactly that and no more.

---

## 22. Validation

Run at minimum:

```text
dotnet restore SteamInputAddonforClaw.slnx
dotnet build SteamInputAddonforClaw.slnx --no-restore -v:minimal
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj --no-build --no-restore -p:IsTestProject=true --logger "console;verbosity=minimal"
node --check src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
git diff --check
```

Also run focused tests for:

```text
AddonQuickSettingsShortcutContractTests
OverlayShortcutSelectionTests
QamFrontendBridgeTests
QamFrontendContractTests
FrontendNamedPipeTransportTests
OverlayTransportTests
```

No new warnings.

---

## 23. Real-device acceptance

On a real MSI Claw:

### QAM

Verify:

- Addon top-level descriptor still exists exactly once;
- Shortcut inner tab opens;
- it shows exactly four shared slots;
- slot labels are Slot 1–Slot 4;
- all show Unassigned;
- no slot performs an action;
- no custom HTML button clone appears;
- Device/Profile/Setting remain usable;
- QAM close/reopen remains stable.

### Overlay

Verify:

- Shortcut remains the existing 2x2 layout;
- labels/status match QAM;
- entering Shortcut selects Slot1;
- DPad/Left Stick/Right Stick navigation keeps existing bounded 2x2 geometry;
- pointer/touch selection still works;
- A remains a no-op on Unassigned slots;
- B closes through the existing OQ4 path;
- LB/RB still change top-level tabs;
- no input leak or presentation switch is caused by Shortcut visibility.

### Cross-surface

Verify the product meaning is identical:

```text
Slot1 / Slot 1 / Unassigned
Slot2 / Slot 2 / Unassigned
Slot3 / Slot 3 / Unassigned
Slot4 / Slot 4 / Unassigned
```

Visual geometry may differ.

---

## 24. Acceptance checklist

- [ ] One shared `AddonQuickSettingsShortcutSlotId` exists.
- [ ] Overlay-local `OverlayShortcutSlotId` is gone.
- [ ] One shared four-slot snapshot/product definition exists.
- [ ] Slot labels are shared.
- [ ] `Unassigned` status is shared.
- [ ] No action/assignment identity is invented.
- [ ] No mutation API is invented.
- [ ] No Shortcut persistence is added.
- [ ] Overlay renders its existing 2x2 shell from the shared product.
- [ ] Overlay 2x2 navigation behavior is unchanged.
- [ ] QAM Shortcut placeholder is replaced.
- [ ] QAM renders four slots from the shared payload.
- [ ] QAM uses existing native `PanelSection` / `PanelSectionRow`.
- [ ] No new Steam native button/tile resolver is added.
- [ ] Shortcut failure is feature-local in QAM.
- [ ] Controller remains placeholder.
- [ ] Device/Profile/Setting behavior is unchanged.
- [ ] Frontend protocol remains v32.
- [ ] Overlay protocol remains v8.
- [ ] Full1902/OQ4 lifecycle is untouched.
- [ ] No polling or generic Shortcut framework is introduced.
- [ ] Full regression suite passes.
- [ ] Real-device QAM/Overlay Shortcut parity acceptance passes.

---

## 25. Review standard

Review this PR against the actual current product.

Blocking issues are realistic defects such as:

- one surface showing different slot identity/order/labels/status;
- QAM Shortcut failure breaking other Addon pages;
- accidental action execution;
- regression in Overlay 2x2 navigation;
- protocol/lifecycle regression;
- duplicated product authority remaining after the migration;
- product scope expanding into unsupported assignment/execution semantics.

Do not block for:

- hypothetical future action types;
- future drag/drop/reorder requirements;
- multi-session support;
- theoretical instruction-level races;
- a desire for a generic tile framework;
- speculative future localization infrastructure.

The target is one small current Shortcut product definition and two thin renderers.
