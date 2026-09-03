# Work Order — OQ5-UI-10: Setting-page Tab Order Editor

## Status

Tenth implementation PR from:

- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`

Track: Addon Quick Settings Overlay UI

Label/name: `OQ5-UI-10`

Phase: Phase C — tab order preference

Implementation baseline:

```text
OQ5-UI-09 merged as PR #471
merge commit: ac1f15af7a9f58aa112fa16b41d0eaba0c67c918
current main at preparation time: 8b2328a50e04a318b6cfad639756ecc808d97735
```

This is **not** part of the numbered Full PID1902 implementation sequence.

---

## 1. Goal

Expose the already-implemented Runtime-owned Overlay tab-order preference through the Overlay `Setting` page.

The user must be able to reorder the five fixed Overlay tabs with either controller navigation or pointer/touch input:

```text
Device
Profile
Controller
Shortcut
Setting
```

Controller contract:

```text
Up / Down   choose one of the five tab-order rows
Left        move that tab one position earlier
Right       move that tab one position later
A           no reorder/edit-mode transition required
B           unchanged: close Overlay globally
LB / RB     unchanged: switch top-level Overlay tab
```

Pointer/touch contract:

```text
Each tab-order row exposes compact Move Earlier / Move Later buttons.
```

The mutation path must be exactly the seam completed by OQ5-UI-08 and OQ5-UI-09:

```text
Setting-page row adjustment
        ↓
construct one proposed five-tab order
        ↓
NamedPipeOverlayClient.SendSetTabOrderAsync(...)
        ↓
Runtime StartupSettingsCoordinator.TryChangeOverlayTabOrder(...)
        ↓
canonical SettingsStore/settings.json
        ↓
Runtime republishes authoritative TabOrderState
        ↓
OverlayWindow.ApplyTabOrder(...)
        ↓
top tab strip + Setting editor rows reflect authoritative order
```

The Overlay must **not** optimistically make its proposal authoritative.

The visible order changes only when the Runtime sends back `TabOrderState`.

---

## 2. Required reading before implementation

Read current `main`, not only the planning examples.

Required project documents:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- current `docs/work-order/` set, especially active Full1902 authority/front-button work
- `docs/overlayui/README.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/OQ5_UI_08_RUNTIME_OWNED_OVERLAY_TAB_ORDER_SETTING_WORK_ORDER.md`
- `docs/overlayui/OQ5_UI_09_OVERLAY_PREFERENCE_TRANSPORT_WORK_ORDER.md`

Required current source:

- `src/SteamInputAddonforClaw.Contracts/Overlay/OverlayTabId.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
- `src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs`
- relevant OQ5 UI tests, especially `OverlayTabStateTests`, `OverlayRowSelectionTests`, `OverlayTabOrderTransportTests`

---

## 3. Current code facts that define the PR boundary

### 3.1 Runtime preference authority already exists

OQ5-UI-08 already owns:

```text
StartupSettingsCoordinator.OverlayTabOrder
StartupSettingsCoordinator.TryChangeOverlayTabOrder(...)
SettingsStore/settings.json
OverlayTabOrderContract
```

PR10 must not add another preference owner, validator, settings file, or persistence path.

### 3.2 Transport already exists and is complete for this PR

OQ5-UI-09 already provides `.Overlay` protocol v5:

```text
Runtime → Overlay: TabOrderState
Overlay → Runtime: SetTabOrder
```

The client already exposes:

```csharp
NamedPipeOverlayClient.SendSetTabOrderAsync(...)
```

The Runtime already republishes the authoritative current order after every well-shaped mutation request, including rejection or persistence failure.

Therefore PR10 requires **no protocol version bump** and no new wire message kind.

### 3.3 Initial authoritative order is already applied before Ready

OQ5-UI-09 guarantees:

```text
HandshakeAccepted
→ TabOrderState
→ Overlay applies order on DispatcherQueue
→ Ready
```

Do not weaken this ordering.

PR10 only adds the visible editor for mutations after the connection is live.

### 3.4 `OverlayWindow` already owns one controller-first row-selection model

Current row interaction is:

```text
Up / Down
→ OverlayRowSelection

Left / Right
→ selected row's OverlayRowCapabilities.Adjust(-1 / +1)

A
→ selected row's OverlayRowCapabilities.Activate
```

The tab-order rows must plug into this existing row model.

Do not build a separate Setting-page navigation engine.

### 3.5 `OverlayWindow.ApplyTabOrder(...)` already preserves the visible top-level tab

Current OQ5-UI-09 behavior:

```text
valid authoritative order
→ replace OverlayTabState order
→ reposition the existing five tab-header Buttons
→ preserve selected top-level tab identity
→ do not reset body scroll/row selection merely because the order changed
```

PR10 extends this same authoritative apply path so the Setting-page editor rows also reflect the returned order.

---

## 4. Frozen product behavior

### 4.1 Exactly five fixed identities

The editor reorders only:

```text
Device
Profile
Controller
Shortcut
Setting
```

It does not add/remove/rename tabs.

Do not implement dynamic tab registration.

### 4.2 First item is the next Show startup tab

Existing product contract remains:

```text
first item in authoritative tab order
→ selected on every subsequent Overlay Show
```

There is still:

```text
no DefaultOverlayTab
no separate startup-tab preference
no last-tab restore
```

While the Overlay is currently visible, reordering must preserve the currently selected top-level tab identity.

The new first tab takes effect only on the next Show.

### 4.3 No reorder mode

Do not introduce:

- A-to-enter reorder mode;
- grab/drop state;
- drag-and-drop requirement;
- modal reorder screen;
- edit/save/cancel transaction;
- long-press gesture.

A selected tab-order row responds directly to Left/Right.

### 4.4 Immediate mutation, not slider debounce

Tab-order moves are discrete commands.

Do **not** route them through `OverlayDelayedSliderCommit` or the 2-second slider trailing-commit policy.

Each valid one-position move should send one `SetTabOrder` request immediately.

### 4.5 Runtime reply remains authoritative

A request means only:

```text
"please move this tab"
```

It does not mean the Overlay may locally commit the new order.

Required behavior:

```text
user requests move
→ compute proposed order
→ send SetTabOrder
→ keep current visual order while waiting
→ Runtime returns TabOrderState
→ apply returned order
```

If the Runtime rejects the request or persistence fails, it republishes the previous order and the Overlay simply remains/returns to that authoritative order.

No separate rollback state is needed because PR10 does not optimistically apply the proposal.

---

## 5. Setting-page layout

Replace the current `Setting` placeholder with a small dedicated section for tab order.

Recommended visual shape:

```text
Setting

Tab Order
────────────────────────────────
Device       [Move Earlier] [Move Later]
Profile      [Move Earlier] [Move Later]
Controller   [Move Earlier] [Move Later]
Shortcut     [Move Earlier] [Move Later]
Setting      [Move Earlier] [Move Later]
```

Use standard WinUI 3 controls and the current Overlay spacing/selected-row visual language.

Compact glyph/icon buttons are acceptable, for example left/right arrows, provided accessible names/tooltips make the action clear.

Do not add a custom drag handle or a new visual framework.

### 5.1 Boundary state

For the current authoritative order:

```text
first row
→ Move Earlier disabled

last row
→ Move Later disabled

middle row
→ both enabled
```

Controller Left on the first item is a bounded no-op.

Controller Right on the last item is a bounded no-op.

A boundary no-op must not send a mutation request.

### 5.2 Pointer/touch uses the same mutation path

The arrow/buttons must call the same narrow move-request method as `OverlayRowCapabilities.Adjust`.

Do not implement a second pointer-only persistence path.

---

## 6. Proposed-order calculation

Keep the calculation narrow and deterministic.

The current authoritative order lives in `OverlayTabState.Order`.

A reasonable small seam is to extend `OverlayTabState` with a pure proposal method conceptually equivalent to:

```csharp
bool TryCreateMovedOrder(
    OverlayTabId tab,
    int delta,
    out IReadOnlyList<OverlayTabId> proposed)
```

Required semantics:

```text
delta must be -1 or +1
find the requested tab in the current authoritative order
boundary move → false / no proposal
otherwise copy current order
swap exactly one adjacent pair
validate/normalize through OverlayTabOrderContract
return proposed order
```

This method must **not** mutate `_order`.

Only `TryApplyOrder(...)` receiving authoritative Runtime state may replace the current order.

An equally small local helper is acceptable if it keeps the same semantics, but do not create a generalized reorder framework.

---

## 7. Overlay → Runtime request seam

`OverlayWindow` currently does not own the transport client; `App` does.

Keep that ownership boundary.

Preferred narrow pattern:

```text
OverlayWindow
→ raises TabOrderChangeRequested(proposedOrder)

App
→ calls _client.SendSetTabOrderAsync(proposedOrder)
```

Exact naming may differ.

Do not pass the whole `NamedPipeOverlayClient` into `OverlayWindow` merely to support this one command.

### 7.1 Send failure

`SendSetTabOrderAsync` returns false when the request frame cannot be written.

Required behavior:

```text
write fails
→ log one narrow warning/error
→ leave current authoritative UI unchanged
→ do not close Overlay merely for this preference failure
→ do not affect Runtime/controller ownership
```

No generic error-dialog framework is required in PR10.

### 7.2 Do not add request correlation machinery

PR09 deliberately uses authoritative state republish as the mutation result.

PR10 must not add:

- request IDs;
- RPC correlation;
- mutation queue manager;
- optimistic revision numbers;
- epochs/barriers;
- generalized preference transactions.

The `.Overlay` pipe already serializes writes and the authoritative state converges after each Runtime-handled request.

Do not add extra machinery solely to defend against an artificially tight sequence of repeated inputs arriving before a local pipe round trip completes. This is not a reason to create another state authority.

---

## 8. Authoritative apply must update both surfaces

After PR10, `OverlayWindow.ApplyTabOrder(authoritativeOrder)` has two UI responsibilities:

```text
1. reposition the five top tab-header buttons
2. reorder the five Setting-page tab-order rows
```

Both must derive from the same already-validated authoritative order.

Do not let the Setting editor maintain a second independent order.

### 8.1 Preserve existing UI instances

As with PR09, do not rebuild the whole Overlay shell on every order change.

Create the five Setting editor rows once and retain them by `OverlayTabId`.

A simple implementation may use a five-row `Grid` and change each row container's `Grid.Row` according to the authoritative order.

This preserves:

- tab-header Button instances;
- page instances;
- tab-order row instances;
- pointer handlers;
- row capability instances.

Do not replace the page or recreate all rows on each mutation.

### 8.2 Preserve selected Setting-row identity during live reorder

This is required for controller usability.

Example:

```text
current order:
Device / Profile / Controller / Shortcut / Setting

selected Setting-page row identity:
Controller

user presses Left

Runtime returns:
Device / Controller / Profile / Shortcut / Setting
```

After authoritative apply:

```text
selected row must still be Controller
```

It must not silently become `Profile` merely because the previous numeric row index is now occupied by another identity.

The selected top-level page also remains `Setting` if it was visible.

### 8.3 Small `OverlayRowSelection` extension is acceptable

Current `OverlayRowSelection.SetRows(...)` always selects the first selectable row.

To preserve the selected identity when the Setting editor itself is reordered, the smallest acceptable extension is conceptually:

```csharp
SetRows(rows, preferredIndex)
```

where:

```text
preferredIndex is valid/selectable
→ retain that index

otherwise
→ existing first-selectable fallback
```

Keep the default call behavior unchanged for normal tab changes.

Do not turn this into a generalized focus history/navigation graph system.

### 8.4 Do not reset scroll merely because order changed

PR09 intentionally made live tab-order apply different from a top-level tab change.

Preserve that rule.

An authoritative reorder while `Setting` is visible must not call the full `ApplySelectedTabVisualState()` path solely to update ordering, because that path resets body scroll and row selection.

Update only what the reorder actually changes, then refresh the selected-row visual using the preserved selected identity.

---

## 9. Selection and row capability behavior

Each tab-order row is always selectable while the Setting page exists.

Its capabilities are:

```text
IsSelectable = true
Adjust(-1)   = request one-position move earlier
Adjust(+1)   = request one-position move later
Activate     = null / no-op
```

Boundary checks happen before sending the request.

`A` therefore does not enter a mode and does not reorder a tab.

Pointer buttons bypass no controller model; they invoke the same row move request method.

---

## 10. Interaction with top-level LB/RB navigation

LB/RB semantics remain unchanged:

```text
LB → previous top-level tab in current authoritative order
RB → next top-level tab in current authoritative order
```

After `TabOrderState` is applied, subsequent LB/RB navigation naturally follows the new order through `OverlayTabState`.

Do not special-case LB/RB while the Setting editor is visible.

Do not consume LT/RT/X/Y for reordering.

---

## 11. Lifecycle / safety invariants

This UI PR must not change any Full1902 or OQ4 ownership path.

Do not modify:

- PID1901/PID1902 switching;
- DirectInput session ownership;
- physical-input acquisition;
- HidHide baseline/reconciliation;
- VIIPER server/bus/device ownership;
- X360/SteamDeck presentation selection;
- OQ4 capture/neutral publication;
- OQ4 release-to-resume consumed-control gate;
- suspend/resume;
- physical device-loss recovery;
- Center M authority transitions;
- WING/OEM1 policy;
- Game Bar suppression;
- Steam QAM coexistence policy.

A tab-order settings mutation failure is an Overlay preference failure only.

The controller Runtime must remain safe and live.

---

## 12. Expected implementation shape

Expected primary files:

```text
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs
src/SteamInputAddonforClaw.Overlay/OverlayRowSelection.cs
```

A small dedicated Overlay-local row class is acceptable if it materially keeps `OverlayWindow.xaml.cs` clearer, for example:

```text
OverlayTabOrderRow.cs
```

but it must remain a fixed tab-order editor row, not a generic reorderable-item framework.

Expected tests:

```text
tests/SteamInputAddonforClaw.Tests/OverlayTabStateTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayRowSelectionTests.cs
```

A focused new test file such as `OverlayTabOrderEditorTests.cs` is acceptable if that is cleaner.

Normally no changes should be necessary in:

```text
FrontendTransport protocol
StartupSettingsCoordinator
SettingsStore
OQ4 controller input router
Full1902 controller lifecycle
```

If implementation appears to require those changes, re-check the PR boundary before expanding scope.

---

## 13. Required tests

Add focused automated coverage for the new editor semantics.

### 13.1 Proposal semantics

Verify:

1. moving a middle tab earlier swaps exactly one adjacent pair;
2. moving a middle tab later swaps exactly one adjacent pair;
3. first-tab Left is a bounded no-op and produces no proposal/request;
4. last-tab Right is a bounded no-op and produces no proposal/request;
5. proposal contains exactly the five known unique tab IDs;
6. creating a proposal does not mutate the current authoritative `OverlayTabState.Order`;
7. only a later `TryApplyOrder(authoritative)` changes the current order.

### 13.2 Row-selection preservation

Verify the smallest `OverlayRowSelection` extension:

```text
valid preferred index
→ selected index preserved

invalid/unselectable preferred index
→ existing first-selectable fallback
```

For the Setting editor mapping, verify conceptually:

```text
selected identity Controller
→ authoritative reorder moves Controller to another row index
→ selected identity after remap is still Controller
```

Do not preserve only the old numeric index.

### 13.3 Existing behavior regressions

Existing tests must continue to prove:

- first authoritative tab is selected on next `ResetForShow()`;
- current selected top-level tab identity survives live `TryApplyOrder`;
- LB/RB traversal uses the current order;
- invalid authoritative order does not corrupt current state;
- `.Overlay` v5 initial state-before-Ready contract remains green;
- `SetTabOrder` mutation reaches Runtime authority and authoritative state is republished;
- command/navigation transport remains usable after preference traffic.

Do not duplicate all PR09 transport tests unless PR10 actually changes transport code.

---

## 14. Manual / hardware validation checkpoint

On a supported MSI Claw build:

1. Open Addon Overlay.
2. Navigate to `Setting` with LB/RB.
3. Confirm the five tab-order rows match the current top-tab order.
4. Use Up/Down to select `Controller` (or another middle row).
5. Press Left once.
6. Confirm the row and top tab strip move only when the authoritative state returns.
7. Confirm the selected Setting-row identity remains the moved tab, not the old numeric row slot.
8. Press Right and confirm it moves back one position.
9. Select the first row and press Left; confirm no change/no error.
10. Select the last row and press Right; confirm no change/no error.
11. Use the pointer/touch Move Earlier/Move Later buttons and confirm they use the same behavior.
12. Move `Setting` itself while the Setting page is visible; confirm the page remains visible and the selected row/scroll position is not reset unnecessarily.
13. Move another tab to the first position.
14. Close Overlay normally.
15. Reopen Overlay and confirm the new first tab is selected immediately with no old/default-tab flash.
16. Repeat several reorder/show/hide cycles and verify OQ4 capture/release behavior and game-facing neutral publication remain unchanged.

No new controller hardware validation is required beyond checking that the existing semantic navigation/capture path was not regressed.

---

## 15. Logging

Keep logging narrow.

Useful events:

```text
TabOrder move requested
- TabId
- Direction

TabOrder request write failed

Authoritative TabOrder applied
```

Do not log every pointer move or every row highlight change at Info level.

Do not add a generic preference telemetry system.

---

## 16. Explicit non-goals

Do not include in OQ5-UI-10:

- OQ5-UI-11 Shortcut 2×2 slot shell;
- Shortcut persistence/action assignment;
- Device/Profile/Controller real feature controls;
- generalized row/card reordering;
- drag/drop framework;
- generic reorderable collection control;
- `DefaultOverlayTab`;
- last-tab persistence;
- a second settings file;
- direct `settings.json` access from Overlay;
- AppSettings-over-IPC;
- `.Overlay` protocol v6;
- new preference message kinds;
- request IDs/RPC/event bus;
- delayed slider commit reuse;
- transport polling;
- another pipe;
- another input session;
- WING/OEM1 mapping decisions;
- Game Bar policy changes;
- Steam QAM visibility management;
- OQ4/Full1902 lifecycle changes.

---

## 17. Review checklist

Review the PR against these concrete questions:

- [ ] Does `Setting` now show exactly five fixed tab-order rows?
- [ ] Do Up/Down use the existing `OverlayRowSelection` model?
- [ ] Does Left/Right move only the selected tab one adjacent position?
- [ ] Are first-Left and last-Right bounded no-ops with no request sent?
- [ ] Do pointer/touch buttons call the same narrow mutation path?
- [ ] Is each valid move sent immediately through the existing PR09 `SendSetTabOrderAsync` seam?
- [ ] Is there no optimistic local authoritative reorder?
- [ ] Does visible order change only from returned `TabOrderState`?
- [ ] Do both the top tab strip and Setting editor derive from that same authoritative state?
- [ ] Is the selected Setting-row **identity** preserved across authoritative reorder?
- [ ] Does live reorder avoid resetting body scroll merely because order changed?
- [ ] Does current top-level tab identity remain visible until the next normal navigation/Show?
- [ ] Does next Show select the new first authoritative tab?
- [ ] Is `.Overlay` still protocol v5 with no new wire semantics?
- [ ] Is Runtime/SettingsStore still the only persistence authority?
- [ ] Are OQ4/Full1902 controller paths untouched?
- [ ] Was no generic reorder/preferences framework introduced?

---

## 18. Completion criteria

OQ5-UI-10 is complete when:

1. the Setting page exposes the five fixed tab identities in current authoritative order;
2. controller Up/Down selects those rows through the existing logical row-selection model;
3. controller Left/Right creates a one-position proposal and sends it through `SendSetTabOrderAsync`;
4. pointer/touch controls invoke the same proposal/request path;
5. boundary moves are no-ops and do not send requests;
6. the Overlay does not optimistically commit its proposal;
7. Runtime-returned `TabOrderState` updates both the top tab strip and Setting editor rows;
8. current top-level tab identity remains selected during live reorder;
9. selected Setting-row identity remains selected when its numeric position changes;
10. body scroll is not unnecessarily reset by live reorder;
11. the new first item becomes the startup tab on the next Show through the existing `ResetForShow()` contract;
12. no new protocol/settings/lifecycle/controller authority is introduced;
13. focused new tests pass;
14. full Release build/test suite passes;
15. `git diff --check` is clean;
16. hardware/manual validation above is completed when a supported Claw is available.

---

## 19. Scope summary

```text
OQ5-UI-10
= Setting-page UI only
+ five fixed tab-order rows
+ controller Left/Right adjacent move requests
+ pointer/touch move buttons
+ existing PR09 SendSetTabOrderAsync seam
+ authoritative-only visual apply
+ preserve selected row identity during live reorder
+ tests
```

The next roadmap item after this PR remains:

```text
OQ5-UI-11 — Shortcut 2×2 slot shell
```
