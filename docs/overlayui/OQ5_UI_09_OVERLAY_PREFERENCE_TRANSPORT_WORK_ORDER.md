# Work Order — OQ5-UI-09: Overlay Preference Transport

## Status

Ninth implementation PR from:

- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`

Track: Addon Quick Settings Overlay UI

Label/name: `OQ5-UI-09`

Overlay-track baseline: OQ5-UI-08 merged as PR #469.

Current repository baseline for implementation:

```text
main @ cb7f4b2a9d9df489a05f221800901c024dfc58a3
OQ5-UI-08: Runtime-owned Overlay tab-order setting (#469)
```

This is **not** part of the numbered Full PID1902 implementation sequence.

---

## 1. Goal

Connect the Runtime-owned `OverlayTabOrder` preference added by OQ5-UI-08 to the already-existing dedicated `.Overlay` transport.

The final PR09 path must be:

```text
Runtime StartupSettingsCoordinator
        │
        │ authoritative OverlayTabOrder
        v
NamedPipeOverlayServer
        │
        │ .Overlay protocol v5
        v
NamedPipeOverlayClient
        │
        │ DispatcherQueue
        v
OverlayWindow / OverlayTabState
        │
        └─ current five-tab shell order
```

And the mutation path prepared for OQ5-UI-10 must be:

```text
future Setting-page reorder UI
        ↓
NamedPipeOverlayClient.SetTabOrder request
        ↓
NamedPipeOverlayServer
        ↓
StartupSettingsCoordinator.TryChangeOverlayTabOrder(...)
        ↓
SettingsStore/settings.json
        ↓
read current authoritative OverlayTabOrder
        ↓
republish authoritative order to Overlay
        ↓
Overlay applies returned order
```

This PR owns only:

```text
Runtime ↔ Overlay tab-order transport
+ authoritative initial snapshot
+ narrow SetTabOrder request seam
+ authoritative state republish
+ Overlay-side order application
+ protocol/tests
```

It does **not** add the Setting-page reorder editor. That remains OQ5-UI-10.

---

## 2. Required reading before implementation

Read current `main` at or after the baseline above.

Required project documents:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- current `docs/work-order/` set, especially the active Full1902 authority/lifecycle work
- `docs/overlayui/README.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/OQ5_UI_08_RUNTIME_OWNED_OVERLAY_TAB_ORDER_SETTING_WORK_ORDER.md`

Required current source:

- `src/SteamInputAddonforClaw.Contracts/Overlay/OverlayTabId.cs`
- `src/SteamInputAddonforClaw/Settings/AppSettings.cs`
- `src/SteamInputAddonforClaw/Settings/SettingsStore.cs`
- `src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`
- `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- `tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs`
- `tests/SteamInputAddonforClaw.Tests/OverlayTabStateTests.cs`
- current OQ5-UI-08 settings/coordinator tests

Do not implement this PR from the older planning examples alone. Current `main` is authoritative.

---

## 3. Current code facts that define this PR boundary

### 3.1 OQ5-UI-08 already owns the preference

Current `StartupSettingsCoordinator` already exposes:

```csharp
public IReadOnlyList<OverlayTabId> OverlayTabOrder => Settings.OverlayTabOrder;

public bool TryChangeOverlayTabOrder(IReadOnlyList<OverlayTabId> requested)
```

Current semantics are already correct:

```text
valid new order
→ validate/copy through OverlayTabOrderContract
→ save SettingsStore first
→ publish Settings = next only after successful save
→ true

valid equal order
→ accepted no-op
→ true

invalid order
→ no save
→ current Settings unchanged
→ false
```

PR09 must call this existing authority.

Do not create another settings owner or another tab-order validator.

### 3.2 Shared identity/invariant already exists

Current shared contract is:

```text
SteamInputAddonforClaw.Contracts.Overlay.OverlayTabId
OverlayTabOrderContract
```

The FrontendTransport project already references the Contracts project.

Therefore `.Overlay` transport may directly carry the shared `OverlayTabId` values.

Do not add:

- wire-only numeric tab IDs;
- duplicate transport enum;
- arbitrary string IDs;
- a generic preference key/value model.

### 3.3 `.Overlay` protocol is currently v4

Current v4 supports only:

```text
Handshake
HandshakeAccepted
Command
Navigation
State
DismissRequested
ProtocolError
```

Current navigation is semantic-only and must remain so.

Current `NamedPipeOverlayClient` sends `Ready` immediately after `HandshakeAccepted`.

That is no longer sufficient once Runtime-owned tab order is required before first Show.

### 3.4 Overlay still builds from the local default order

Current `OverlayTabState` owns an immutable `_order` initialized at construction.

Current `OverlayWindow`:

```text
constructs OverlayTabState()
→ BuildShell()
→ builds the five tab buttons/pages once from that order
```

Therefore merely adding a JSON field to `OverlayWireMessage` is not enough.

PR09 must add one narrow apply seam so the warm Overlay can consume the Runtime order and update the existing shell without rebuilding the entire UI architecture.

---

## 4. Frozen product/authority contract

### 4.1 Runtime remains the only preference authority

The authoritative preference is:

```text
StartupSettingsCoordinator.OverlayTabOrder
```

Persistence remains:

```text
SettingsStore
→ canonical settings.json
```

`SteamInputAddonforClaw.Overlay.exe` must never:

- read `settings.json` directly;
- write `settings.json` directly;
- create `overlay-settings.json`;
- persist a second copy;
- infer authority from its local order;
- poll the Runtime for settings.

### 4.2 One narrow preference only

PR09 transports only:

```text
OverlayTabOrder
```

Do not expose:

```text
AppSettings
Dictionary<string, object>
GetPreference(key)
SetPreference(key, value)
OverlayPreferencesManager
frontend settings RPC reuse
```

Future Overlay features should add explicit contracts when they actually need them.

### 4.3 No controller/lifecycle authority change

This PR must not change:

- PID1901 / PID1902;
- DirectInput ownership;
- HidHide;
- VIIPER server/bus/device ownership;
- Xbox360 / SteamDeck presentation selection;
- OQ4 capture / neutral publication;
- release-to-resume consumed-control gate;
- physical-input recovery;
- suspend/resume;
- Center M authority;
- mandatory Runtime lifetime;
- Steam/BPM semantics;
- WING/OEM1 policy.

The Overlay remains a subordinate UI process. A preference transport failure is feature-local and must never become a controller safety or startup authority failure.

### 4.4 Current Steam-QAM coexistence policy remains unchanged

Do not add QAM visibility management as part of preference transport.

Current policy remains:

```text
Steam QAM may remain visible behind Addon Overlay
→ OQ4 capture keeps game-facing controller output neutral
→ Overlay closes
→ underlying Steam QAM remains available
```

No `.Qam` changes are required.

---

## 5. Protocol v5

Bump:

```text
OverlayTransportProtocol.CurrentVersion
4 → 5
```

A v4 peer must fail the handshake. Do not add v4 compatibility fallback.

### 5.1 Add exactly two tab-order message semantics

Preferred narrow shape:

```text
TabOrderState     Runtime → Overlay
SetTabOrder       Overlay → Runtime
```

Exact enum member names may differ if current naming conventions make another name clearer, but do not generalize them into `Preferences`, `Settings`, or RPC method dictionaries.

Extend `OverlayWireMessage` with one optional payload conceptually equivalent to:

```csharp
IReadOnlyList<OverlayTabId>? TabOrder = null
```

An array is also acceptable if it makes JSON serialization/test code cleaner.

Use the existing `JsonStringEnumConverter(... allowIntegerValues: false)` behavior so tab identities cross the wire as names.

### 5.2 Strict message shapes

`TabOrderState` must contain:

```text
ProtocolVersion = v5
Kind            = TabOrderState
TabOrder        = complete valid five-tab order
Command         = null
Navigation      = null
State           = null
Error           = null
```

`SetTabOrder` must contain:

```text
ProtocolVersion = v5
Kind            = SetTabOrder
TabOrder        = requested five-tab order
Command         = null
Navigation      = null
State           = null
Error           = null
```

Existing Command / Navigation / State / DismissRequested shape validation must remain strict.

Do not make unrelated fields optional in ways that allow ambiguous mixed-purpose frames.

---

## 6. Initial handshake ordering — authoritative order before `Ready`

This is the most important PR09 transport rule.

Current v4 flow is roughly:

```text
Client → Handshake
Server → HandshakeAccepted
Client → Ready
```

PR09 must become:

```text
Client → Handshake
Server → HandshakeAccepted
Server → TabOrderState(current authoritative order)
Client validates order
Client applies order on Overlay DispatcherQueue
Client → Ready
```

Only after the authoritative order has been successfully applied may the Overlay client report `Ready`.

Reason:

```text
warm Overlay process starts hidden
→ Runtime already has custom persisted order
→ if Ready is published before preference application,
   Runtime can immediately Show the surface using the default shell order
→ user may see the wrong startup tab/order for a frame or an entire interaction
```

Required invariant:

> `NamedPipeOverlayServer.WaitForReadyAsync()` must not succeed until the client has completed the initial Runtime tab-order application.

### 6.1 Failure behavior

If the initial state cannot be decoded, validated, or applied:

```text
client does not publish Ready
→ current Overlay connection/startup fails feature-locally
→ Runtime/controller continues
→ next explicit Overlay attempt may relaunch/reconnect
```

Do not fall back to a silently independent local preference after a valid Runtime authority already exists.

The server should normalize/read its current order through the shared contract before sending, so an invalid authoritative payload should not normally be possible.

---

## 7. Runtime authority plumbing

`FrontendTransport` must not gain a dependency on `StartupSettingsCoordinator` or the Settings namespace.

Pass only the two narrow operations needed by the `.Overlay` server:

```text
read current authoritative tab order
try to change requested tab order
```

Conceptually:

```csharp
Func<IReadOnlyList<OverlayTabId>> getTabOrder
Func<IReadOnlyList<OverlayTabId>, bool> tryChangeTabOrder
```

Exact delegate placement is flexible.

### 7.1 Use the existing composition's one `StartupSettingsCoordinator`

Current `AddonRuntimeComposition` already contains:

```text
StartupSettingsCoordinator StartupSettings
```

`AddonProcessHost.InitializeRuntimeAsync()` receives that exact instance before it starts warm Overlay startup.

Use that instance.

Do not construct another:

```text
SettingsStore
StartupSettingsCoordinator
settings reader
preference service
```

solely for Overlay.

### 7.2 Preferred host/controller seam

Use the smallest explicit wiring that keeps layering clean.

A reasonable implementation is:

```text
AddonProcessHost
→ retain/bind the current composition.StartupSettings
→ give OverlayProcessController two narrow delegates
→ OverlayProcessController creates NamedPipeOverlayServer with those delegates
```

Do not pass the entire `StartupSettingsCoordinator` into FrontendTransport.

Do not create `OverlayPreferenceAuthorityManager` or another owner class merely to hold two delegates.

### 7.3 Mutation operation failure is feature-local

`TryChangeOverlayTabOrder` may throw if persistence itself fails.

At the Runtime/Overlay adapter boundary:

```text
settings mutation throws
→ log one narrow warning/error
→ report mutation not accepted
→ keep current StartupSettingsCoordinator.Settings unchanged by its existing save-then-current contract
→ republish the current authoritative order if the connection is still usable
→ do not affect controller Runtime
```

Do not let a settings file write failure tear down PID1902/DirectInput/VIIPER ownership.

---

## 8. Mutation request contract for OQ5-UI-10

PR09 adds the transport seam that OQ5-UI-10 will call later.

Add a narrow client method conceptually equivalent to:

```csharp
Task<bool> SendSetTabOrderAsync(IReadOnlyList<OverlayTabId> requested, CancellationToken token = default)
```

The return value only needs to mean that the request frame was accepted for transport/write. The authoritative result is the Runtime state that comes back afterward.

Do not build a general request-ID/RPC/correlation framework for one preference.

### 8.1 Server handling

When the server receives a well-shaped `SetTabOrder` request:

```text
requested order
→ StartupSettingsCoordinator.TryChangeOverlayTabOrder(requested)
→ regardless of accepted/rejected no-op/invalid result,
   read StartupSettingsCoordinator.OverlayTabOrder again
→ send TabOrderState(authoritative current order)
```

This means the authoritative state reply is the mutation result.

Examples:

```text
valid changed request
→ persisted
→ returned state == requested normalized order

valid equal request
→ accepted no-op
→ returned state == current/requested order

invalid request
→ no persistence
→ returned state == previous authoritative order

persistence failure
→ Runtime logs failure
→ returned state == previous authoritative order when transport remains usable
```

Do not optimistically make the Overlay's local order authoritative merely because it sent a request.

### 8.2 No `OverlayTabOrderChanged` event is required in this PR

There is currently no second Runtime mutation path for this preference.

OQ5-UI-10 will use the same `.Overlay` request seam.

Therefore do not add a general settings-change event/event bus solely for hypothetical future writers.

If a later real desktop-UI mutation path is added, add the smallest notification required at that time.

---

## 9. Overlay client dispatch

Extend `NamedPipeOverlayClient.RunAsync(...)` with one narrow tab-order state handler.

Conceptually:

```csharp
Func<IReadOnlyList<OverlayTabId>, Task> tabOrderHandler
```

The handler must be used for:

- the mandatory initial snapshot before Ready;
- any authoritative state republished after a future mutation request.

Do not create a generic message/event dispatch registry.

### 9.1 Dispatcher ordering

`App.xaml.cs` must marshal tab-order application through the existing `DispatcherQueue` and return a Task that completes only after the UI state has actually been applied.

For the initial snapshot:

```text
transport thread receives TabOrderState
→ enqueue Overlay UI application
→ await UI apply completion
→ only then client sends Ready
```

Do not fire-and-forget the initial preference application.

For later authoritative state updates, use the same handler/seam.

---

## 10. Make `OverlayTabState` accept authoritative order updates

Current `_order` is readonly.

Add the smallest mutable-order operation required by PR09.

Conceptually:

```csharp
bool TryApplyOrder(IReadOnlyList<OverlayTabId> order)
```

Required behavior:

```text
valid complete order
→ copy/normalize using OverlayTabOrderContract
→ replace current order
→ preserve current selected tab identity
→ true

invalid order
→ do not mutate current order
→ false
```

Keep current constructor fallback behavior if useful for local defensive construction/tests.

Do not duplicate the five-tab validation algorithm; use `OverlayTabOrderContract`.

### 10.1 Preserve selected identity on a live order update

All five fixed identities remain present in every valid order.

If current selected tab is `Setting` and the order changes:

```text
selected tab remains Setting
```

The new first tab is used on the **next Show**, because existing frozen policy remains:

```text
ResetForShow() → current Order[0]
```

Do not force the UI to jump to the new first tab merely because the preference changed while the Overlay was visible.

---

## 11. Apply order to the existing shell without rebuilding pages

Current `OverlayWindow.BuildShell()` creates the five tab buttons/pages once.

PR09 should keep that lifetime.

When an authoritative order arrives:

```text
OverlayTabState.TryApplyOrder(order)
→ reposition existing tab header buttons according to the new order
→ preserve existing page instances
→ preserve selected tab identity
```

The current shell already uses five fixed identities and dictionaries keyed by `OverlayTabId`, so there is no need to destroy/recreate:

- page content;
- row primitives;
- slider delayed-commit helper;
- row-selection model;
- tab button instances.

For the tab header, update the existing layout position, e.g. the `Grid.Column` of each known tab button, using the authoritative ordered list.

Do not build a dynamic tab collection framework.

### 11.1 Do not reset row/scroll state solely because order changed

A future OQ5-UI-10 reorder will occur while the Setting page is visible.

Therefore applying a new tab order must not unnecessarily call the current full tab-change path that resets:

- `BodyScroll` to top;
- selected row to first selectable row.

Order-only update should change tab header position/ordering and selected-header visual state while preserving the currently visible selected page/row/scroll position.

A real tab identity change through LB/RB/click continues to use the existing tab-change behavior.

### 11.2 Initial snapshot remains invisible

The first Runtime order is applied while the Overlay is still hidden and before `Ready`.

Therefore the first Show naturally executes:

```text
ResetForShow()
→ authoritative Order[0]
→ ApplySelectedTabVisualState()
→ visual reveal
```

No default-order flash is acceptable.

---

## 12. Existing transport behavior must remain intact

PR09 must preserve:

```text
Show / Hide acknowledgement
Shutdown
semantic Navigation
DismissRequested
CurrentUserOnly pipe boundary
single Overlay client
shared write gate
bounded command timeout
warm hidden process lifecycle
unexpected visible-session loss handling
```

Tab-order messages are not controller navigation and are not gated on Overlay visibility.

The preference snapshot may be delivered while hidden because it is UI configuration state, not an interaction event.

Do not add:

- another pipe;
- polling;
- another background thread solely for preferences;
- a multi-client rewrite;
- a generic event bus.

---

## 13. Expected source changes

Likely touched files:

```text
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs

tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayTabStateTests.cs
```

Potentially add one small focused test file if that is cleaner than making the existing transport test file too large.

No change is expected to:

```text
SettingsStore persistence format
AppSettings shape
OverlayTabId / OverlayTabOrderContract identities
OverlayControllerInputRouter
Full1902 physical ownership
HidHide
VIIPER
QamHost / .Qam
Main desktop .Frontend protocol
```

Do not modify those areas merely to route one preference.

---

## 14. Tests

Use the existing test project and existing pipe-test patterns.

### A. Protocol v4 peer is rejected by v5

Prove a v4 Overlay peer cannot handshake with v5.

No compatibility fallback.

### B. Initial authoritative order is delivered before Ready

Use a non-default Runtime order such as:

```text
Controller, Device, Profile, Shortcut, Setting
```

Prove:

```text
client receives that exact order
client handler is entered
server does NOT become Ready while the handler is deliberately blocked
handler completes
client sends Ready
server WaitForReadyAsync succeeds
```

This test proves the first-frame/startup-tab invariant at the transport boundary.

### C. Initial order round-trips enum names

Prove wire JSON uses tab enum names rather than numeric IDs.

### D. Mutation request reaches the Runtime authority seam

Using a fake current-order provider/mutator:

```text
client sends valid custom order
→ server mutator sees exactly that order
→ fake authoritative state changes
→ server republishes TabOrderState
→ client handler receives the authoritative changed order
```

### E. Rejected request republishes current authority

Use a malformed complete-list request such as duplicate tab IDs through a raw test frame or the narrowest suitable seam.

Prove:

```text
mutator rejects
→ authoritative state remains previous order
→ server returns previous authoritative order
→ connection remains usable
```

The existing OQ5-UI-08 coordinator tests remain the authority for persistence rejection semantics.

### F. Command/navigation remain usable after preference traffic

After initial snapshot and at least one tab-order mutation/state reply:

- Show still acknowledges Visible;
- semantic Navigation still arrives intact;
- Hide still acknowledges Hidden;
- Shutdown still completes.

### G. Invalid/mixed preference message shapes are rejected

Cover representative malformed shapes:

- `TabOrderState` without TabOrder;
- `SetTabOrder` without TabOrder;
- preference message mixed with Command/Navigation/State payload;
- unknown numeric tab enum value if the codec can be exercised directly.

### H. `OverlayTabState` applies a valid new order

Prove:

```text
initial default
→ select Setting
→ apply Controller, Device, Profile, Shortcut, Setting
→ Order updated exactly
→ SelectedTab still Setting
→ ResetForShow()
→ SelectedTab becomes Controller
```

### I. `OverlayTabState` rejects invalid live update without corrupting current state

Prove duplicate/missing/unknown update:

```text
TryApplyOrder == false
current Order unchanged
current SelectedTab unchanged
```

### J. Existing Overlay tests remain green

Preserve current coverage for:

- endpoint separation;
- oversized frame rejection;
- Show/Hide/Shutdown;
- no unsolicited Hidden state after Ready;
- immediate Show acknowledgement;
- semantic Navigation delivery;
- DismissRequested;
- OQ4 Runtime-owned retirement signaling.

Do not weaken those tests to make the preference transport pass.

---

## 15. Hardware/manual validation checkpoint

This PR does not need new controller feature hardware logic, but the completed transport should be checked on a supported MSI Claw before calling the UI behavior fully validated.

Suggested manual check:

1. Put a valid non-default `OverlayTabOrder` into the canonical `settings.json`, or use a focused test/dev seam without adding production UI.
2. Start/restart the Runtime so OQ5-UI-08 loads that order.
3. Allow warm `Overlay.exe` startup.
4. Open Overlay.
5. Verify the first visible tab is the first persisted tab with no default-tab flash.
6. Move to another tab and close Overlay.
7. Reopen Overlay.
8. Verify it again opens to authoritative `OverlayTabOrder[0]`, not the last selected tab.
9. Repeatedly Show/Hide and verify OQ4 capture/release behavior is unchanged.

Do not add temporary production settings UI solely for this validation.

---

## 16. Explicit non-goals

Do not include in OQ5-UI-09:

- Setting-page tab-order editor — OQ5-UI-10;
- drag/drop reorder;
- Shortcut slot settings;
- Device/Profile/Controller real feature transport;
- a generic Overlay settings snapshot;
- full `AppSettings` transport;
- `OverlayTabOrderChanged` event bus without a real second writer;
- request IDs / generic RPC correlation framework;
- multiple in-flight preference transaction manager;
- polling;
- settings-file watcher;
- Overlay-owned persistence;
- QAM visibility management;
- WING/OEM1 mapping;
- controller capture changes;
- PID/HidHide/VIIPER changes;
- visual compacting/typography polish.

If OQ5-UI-10 later needs one-at-a-time UI mutation gating, implement that at the narrow editor/client seam rather than pre-building a generic transaction system here.

---

## 17. Logging

Keep logs low-rate and preference-specific.

Useful Runtime-side events:

```text
Overlay initial tab-order state prepared/sent
Overlay SetTabOrder accepted/rejected
Overlay tab-order persistence failure
```

Useful Overlay-side events:

```text
authoritative tab order received/applied
SetTabOrder request send failure
```

Do not log every tab navigation action at Info solely because preference transport exists.

Do not log the whole settings file.

---

## 18. Review checklist

A reviewer should be able to answer **yes** to all of the following.

### Authority

- Is `StartupSettingsCoordinator` still the only tab-order persistence/mutation authority?
- Does Overlay avoid direct settings-file access?
- Is the shared `OverlayTabId`/`OverlayTabOrderContract` reused instead of duplicated?

### Handshake

- Does Runtime send the current order after handshake acceptance?
- Does Overlay fully apply that initial order before sending Ready?
- Can Runtime therefore never intentionally Show a Ready Overlay that still has only the wrong local default order?

### Mutation

- Does SetTabOrder route into `TryChangeOverlayTabOrder`?
- Is current authoritative state republished after every mutation attempt?
- Does a rejected/failed mutation leave the previous order authoritative?

### UI state

- Can `OverlayTabState` change its order without changing selected tab identity?
- Does the next Show use the new `Order[0]`?
- Does a live order update avoid resetting the selected row/scroll position unnecessarily?
- Are existing page/control instances preserved?

### Scope

- Is `.Overlay` the only protocol changed?
- Are `.Frontend` and `.Qam` untouched?
- Are OQ4 capture and Full1902 controller ownership untouched?
- Is there no generic settings/event/RPC framework added for one preference?

---

## 19. Completion criteria

Implementation is complete only when:

- `.Overlay` protocol is explicitly v5;
- a v4 peer is rejected;
- current Runtime `OverlayTabOrder` is sent on every new Overlay connection;
- Overlay applies that order before Ready;
- first Show therefore resets to authoritative `Order[0]`;
- a narrow SetTabOrder request exists for OQ5-UI-10;
- Runtime routes mutation through existing `StartupSettingsCoordinator.TryChangeOverlayTabOrder`;
- authoritative current order is republished after mutation attempts;
- Overlay can apply republished order without rebuilding page/control ownership;
- selected tab identity is preserved on live reorder;
- next Show uses the new first tab;
- no direct Overlay settings persistence exists;
- no controller/Full1902 lifecycle behavior changed;
- relevant tests pass;
- full Release build/test suite is clean;
- `git diff --check` is clean.

---

## 20. Final implementation principle

The intended architecture after OQ5-UI-09 is:

```text
one Runtime settings authority
        ↓
one narrow OverlayTabOrder contract
        ↓
one dedicated .Overlay connection
        ↓
one warm Overlay shell consuming authoritative state
```

Keep it that small.

PR09 is a preference transport seam, not the beginning of a generalized settings framework.