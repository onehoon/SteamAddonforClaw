# Work Order — OQ5-UI-08: Runtime-Owned Overlay Tab-Order Setting

## Status

Eighth implementation PR from:

- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`

Track: Addon Quick Settings Overlay UI

Label/name: `OQ5-UI-08`

Overlay-track baseline: OQ5-UI-07 merged as PR #467.

Current repository baseline for implementation: `main` after PR #468 / commit `4a5b872f0cc8670144fad452d7fd662c2783b4c8`.

PR #468 is unrelated Full1902 policy work, but it changed `AppSettings` / `SettingsStore`, so implementations must use the **current** settings shape rather than the pre-#468 examples from older work orders.

This is **not** part of the numbered Full PID1902 implementation sequence.

---

## 1. Goal

Add exactly one persisted Overlay preference to the existing Runtime settings authority:

```text
OverlayTabOrder
```

It represents the order of the five fixed top-level Overlay tabs:

```text
Device
Profile
Controller
Shortcut
Setting
```

Default:

```text
Device, Profile, Controller, Shortcut, Setting
```

The first entry in this list is, by existing UI contract, the tab that will be selected on every future Overlay Show **once OQ5-UI-09 transports the authoritative order to Overlay.exe**.

This PR owns only:

```text
shared tab identity/invariant
+ Runtime AppSettings state
+ settings.json persistence
+ Runtime mutation seam
+ deterministic validation/defaulting
```

It does **not** yet send the preference to `SteamInputAddonforClaw.Overlay.exe` and does **not** add the Setting-page reorder UI.

---

## 2. Required reading before implementation

Read current `main` after PR #468.

Required project documents:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/` current work-order set, especially the active Full1902 authority/lifecycle work orders
- `docs/overlayui/README.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/overlayui/OVERLAY_UI_IMPLEMENTATION_PR_PLAN.md`
- `docs/overlayui/OQ5_UI_01_FIVE_TAB_OVERLAY_SHELL_WORK_ORDER.md`
- `docs/overlayui/OQ5_UI_04_LOGICAL_ROW_SELECTION_SCROLLING_WORK_ORDER.md`
- `docs/overlayui/OQ5_UI_07_SHARED_DELAYED_SLIDER_COMMIT_WORK_ORDER.md`

Required current source:

- `src/SteamInputAddonforClaw/Settings/AppSettings.cs`
- `src/SteamInputAddonforClaw/Settings/SettingsStore.cs`
- `src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs`
- `src/SteamInputAddonforClaw.Overlay/SteamInputAddonforClaw.Overlay.csproj`
- `src/SteamInputAddonforClaw.Contracts/`
- `tests/SteamInputAddonforClaw.Tests/SettingsStoreTests.cs`
- `tests/SteamInputAddonforClaw.Tests/OverlayTabStateTests.cs`

Do not copy an older `AppSettings` shape from a previous work order. PR #468 removed `SteamInputRoutingEnabled`; current main is authoritative.

---

## 3. Frozen product contract

### 3.1 Five fixed identities only

The only known top-level tabs are:

```text
Device
Profile
Controller
Shortcut
Setting
```

The user may change **only their order**.

Do not add:

- user-created tabs;
- deleted/hidden tabs;
- plugin tabs;
- nested tabs;
- arbitrary string IDs;
- generic dashboard layout persistence.

### 3.2 Exactly one ordered list

Persist one complete ordered collection containing all five identities exactly once.

Valid example:

```text
Controller, Device, Profile, Shortcut, Setting
```

Invalid examples:

```text
Device, Profile, Controller                 // missing tabs
Device, Profile, Controller, Shortcut, Shortcut // duplicate
Device, Profile, Controller, Shortcut, Unknown  // unknown
[]
```

### 3.3 No separate startup/default tab

Do **not** persist any second preference such as:

```text
DefaultOverlayTab
OverlayStartupTab
LastOverlayTab
SelectedOverlayTab
```

Existing frozen rule remains:

```text
OverlayTabOrder[0] == startup tab on every Show
```

### 3.4 Do not persist transient navigation state

Do not persist:

- selected row;
- per-tab selected row;
- scroll offset;
- last visible page;
- slider drafts/pending commits.

Those are transient Overlay interaction state, not Runtime preferences.

---

## 4. Use one shared tab identity contract

### 4.1 Do not create a second Runtime-only tab enum

Current `OverlayTabState.cs` contains an internal `OverlayTabId` enum with the correct five identities.

Adding a different settings-layer enum with the same five names would create two identity authorities immediately before OQ5-UI-09 needs to carry the same fact across the process boundary.

Instead, move/establish the fixed identity in the existing shared Contracts assembly, for example:

```text
src/SteamInputAddonforClaw.Contracts/Overlay/OverlayTabId.cs
```

Conceptual shape:

```csharp
namespace SteamInputAddonforClaw.Contracts.Overlay;

public enum OverlayTabId
{
    Device,
    Profile,
    Controller,
    Shortcut,
    Setting,
}
```

Then make current `OverlayTabState` use this shared identity.

This is **not** OQ5-UI-09 transport implementation. It is only eliminating duplicate identity before persistence and transport both need the same five values.

### 4.2 Keep one narrow order-validation rule

The same fixed-list invariant is needed by:

- persisted settings load;
- Runtime mutation validation;
- existing OverlayTabState local normalization.

Do not copy the same five-tab validation algorithm into three places.

Add one small shared/domain helper near the shared tab identity, conceptually:

```csharp
OverlayTabOrderContract.TryNormalize(...)
OverlayTabOrderContract.DefaultOrder
```

Exact naming is flexible.

Required behavior:

```text
input contains all five known tabs exactly once
→ return an immutable/read-only copy in requested order

otherwise
→ report invalid
```

Also provide the simplest defaulting seam required by persistence/local shell code, e.g.:

```text
NormalizeOrDefault(...)
```

Do not build:

- a migration framework;
- generic enum-list validation utilities;
- a settings schema engine;
- a reorder collection abstraction.

This helper is specifically for the five Overlay tabs.

### 4.3 Default order must not be externally mutable

Do not expose a mutable static array that callers can modify globally.

Use a read-only collection or return defensive copies.

Frozen default is exactly:

```text
Device
Profile
Controller
Shortcut
Setting
```

---

## 5. Align current `OverlayTabState` with the shared contract

Current `OverlayTabState` already has the correct behavioral model:

```text
ordered five tabs
selected tab
ResetForShow() → order[0]
bounded Previous/Next
invalid local order → frozen default
```

Preserve all of that.

Required refactor only:

- remove the private duplicate `OverlayTabId` declaration from `OverlayTabState.cs`;
- import/use the shared Contracts identity;
- make `OverlayTabState.DefaultOrder` derive from the shared default contract;
- make its constructor normalization delegate to the shared five-tab validation/defaulting rule rather than carrying a second copy of the algorithm.

Do not otherwise redesign `OverlayTabState`.

No UI layout, selection, LB/RB behavior, or Show behavior changes are required.

If direct consumption of the Contracts assembly requires an explicit project reference in `SteamInputAddonforClaw.Overlay.csproj`, add the direct reference. Do not create another project solely for this type.

---

## 6. Extend current `AppSettings` without breaking positional callers

Current main after PR #468 is:

```csharp
public sealed record AppSettings(
    bool LaunchAtWindowsStartup = true,
    AppLogPreference LogLevel = AppLogPreference.Off,
    bool SuppressDeveloperMenuWarning = false)
{
    public bool DeveloperMenuEnabled { get; init; }
    public Oem1MappingSettings Oem1Mapping { get; init; } = Oem1MappingSettings.Default;
    public WingMappingSettings WingMapping { get; init; } = WingMappingSettings.Default;
}
```

Follow the existing OEM1/WING compatibility pattern: add Overlay tab order as an **init-only property**, not another positional constructor parameter.

Conceptually:

```csharp
public IReadOnlyList<OverlayTabId> OverlayTabOrder { get; init; }
    = OverlayTabOrderContract.DefaultOrder;
```

Exact collection type may differ if needed for clean serialization, but the in-memory value must be treated as a complete normalized order, not a mutable settings editor list.

Do not add a separate `OverlaySettings` root object merely for one preference.

---

## 7. Persist through the existing `SettingsStore/settings.json`

### 7.1 Canonical JSON shape

Persist at the existing settings root under one property:

```json
{
  "OverlayTabOrder": [
    "Device",
    "Profile",
    "Controller",
    "Shortcut",
    "Setting"
  ]
}
```

The real file also contains the existing settings properties.

Use enum **names**, not numeric values.

The existing `SettingsStore.SerializerOptions` already uses `JsonStringEnumConverter(... allowIntegerValues: false)` for named enum persistence. Reuse it.

Update any stale comment claiming OEM1 is the only payload that uses the converter.

### 7.2 Preserve every existing setting on save

Current `SettingsStore.Save` writes an explicit anonymous payload.

Add `OverlayTabOrder` to that payload.

Do not accidentally drop:

- `LaunchAtWindowsStartup`;
- `LogLevel`;
- `SuppressDeveloperMenuWarning`;
- `DeveloperMenuEnabled`;
- `Oem1Mapping`;
- `WingMapping`.

Existing settings mutations must preserve a previously saved custom Overlay order.

---

## 8. Load behavior: invalid persistence falls back locally

### 8.1 Missing setting

For a pre-OQ5-UI-08 settings file with no `OverlayTabOrder`:

```text
load succeeds
→ OverlayTabOrder = frozen default
→ all unrelated existing settings keep their persisted values
```

Do not immediately rewrite the file just to add the default property.

### 8.2 Invalid tab-order value

The following must resolve only this preference to default:

- property is not an array;
- wrong count;
- duplicate tab;
- missing tab;
- unknown enum name;
- numeric enum value;
- null element / malformed element.

Required outcome:

```text
invalid OverlayTabOrder
→ log a narrow warning if useful
→ OverlayTabOrder = default
→ preserve all other readable settings
→ Runtime startup continues
```

Do not let a malformed tab-order property throw into the outer `SettingsStore.Load()` catch and reset every unrelated setting to defaults.

Use a small local `ReadOverlayTabOrder(root)`-style parser, analogous in scope to the current nested OEM1/WING readers.

### 8.3 Entire settings file invalid JSON

Keep existing behavior unchanged:

```text
whole JSON document cannot be parsed
→ SettingsStore existing top-level fallback applies
```

This PR does not redesign global settings corruption recovery.

---

## 9. Mutation behavior: invalid requests are rejected, not converted to default

Persistence recovery and user mutation are intentionally different cases.

### Persisted corrupt/missing value

```text
invalid stored value
→ recover to default
```

### Future Overlay reorder request

```text
invalid requested order
→ reject request
→ keep current authoritative order unchanged
→ do NOT silently replace it with default
```

Add a narrow Runtime settings mutation seam to `StartupSettingsCoordinator`, for example:

```csharp
public IReadOnlyList<OverlayTabId> OverlayTabOrder => Settings.OverlayTabOrder;

public bool TryChangeOverlayTabOrder(IReadOnlyList<OverlayTabId> requested)
```

Exact naming is flexible.

Required semantics:

```text
valid new order
→ normalize/copy through shared contract
→ build next AppSettings
→ persist through existing SettingsStore
→ publish Settings = next only after successful save
→ return accepted

valid order equal to current
→ accepted no-op

invalid order
→ rejected
→ no disk mutation
→ current Settings unchanged
```

Maintain the existing `StartupSettingsCoordinator` save-then-current-state pattern used by OEM1/WING for settings where Runtime state must not claim a value that failed to persist.

Do not add a generic settings mutation dictionary/API.

### 9.1 No change event is required yet

Do not add `OverlayTabOrderChanged` merely in anticipation of future consumers unless implementation evidence proves OQ5-UI-09 needs it.

OQ5-UI-09 will own the actual Overlay transport/update delivery contract.

This PR only needs a deterministic read + mutation seam that UI09 can call later.

---

## 10. Current Overlay behavior intentionally does not change yet

After this PR, a custom order may exist in Runtime settings, but there is still no authoritative settings transport from Runtime to Overlay.

Therefore this PR must **not** fake the integration by allowing `Overlay.exe` to read `settings.json` directly.

Current result is intentionally:

```text
Runtime settings can store custom tab order
Overlay.exe still uses its local/shared default order
```

until OQ5-UI-09.

This temporary disconnect is the planned PR boundary.

Do not add:

- direct file reads from `Overlay.exe`;
- environment variables/command-line order passing;
- duplicated settings file access;
- polling;
- a temporary protocol message that UI09 immediately replaces.

---

## 11. `.Overlay` protocol remains unchanged

OQ5-UI-09 is explicitly the transport PR.

Therefore OQ5-UI-08 must not:

- bump `.Overlay` protocol v4;
- add `SetTabOrder` RPC/messages;
- send tab-order snapshots;
- expose whole `AppSettings` over `.Overlay`;
- modify OQ4 controller navigation/capture messages.

No change is expected to:

```text
OverlayWire.CurrentVersion
OverlayControllerInputRouter
OQ4 capture/release gate
```

---

## 12. Controller / Full1902 lifecycle remains untouched

This is a normal user preference only.

It must not affect:

- PID1901 / PID1902;
- DirectInput ownership;
- HidHide;
- VIIPER server/bus;
- Xbox360 / SteamDeck presentation selection;
- OQ4 neutral publication;
- suspend/resume;
- device loss recovery;
- Runtime mandatory lifetime;
- MSI Center M authority;
- Steam/BPM session semantics.

A malformed tab preference can never be a reason for Runtime startup failure or controller teardown/recovery behavior.

---

## 13. Tests

Use the current normal test project. No new test framework is required.

### A. Shared default contract

Prove default contains exactly, in order:

```text
Device
Profile
Controller
Shortcut
Setting
```

and all five are unique.

### B. Shared validation accepts custom complete order

Example:

```text
Controller, Device, Profile, Shortcut, Setting
```

remains exactly that order.

### C. Shared validation rejects malformed order

Cover at least:

- empty;
- missing entries;
- duplicate;
- unknown enum value.

### D. Existing OverlayTabState behavior remains intact

Current tests must remain green for:

- default order;
- custom-order ResetForShow → order[0];
- bounded LB/RB traversal;
- invalid local order fallback.

Update imports/types only as required by the shared contract move.

### E. Missing persisted preference defaults without losing other settings

Load a legacy/preference-missing JSON file with at least one non-default unrelated setting and prove:

```text
other setting preserved
OverlayTabOrder == default
```

### F. Valid custom order round-trips

Save and load a custom order.

Prove:

- order preserved exactly;
- JSON contains string enum names;
- JSON does not contain numeric tab IDs.

### G. Invalid persisted order falls back locally

At minimum cover representative cases for:

- duplicate/missing list;
- unknown string;
- numeric enum;
- wrong JSON kind.

For every case, prove an unrelated setting such as `LaunchAtWindowsStartup = false` or `LogLevel = Debug` remains preserved.

### H. Valid Runtime mutation persists

Create `StartupSettingsCoordinator`, submit a valid custom order, and prove:

```text
mutation accepted
coordinator current value == custom order
SettingsStore.Load() == custom order
```

### I. Invalid Runtime mutation is rejected without state change

Start from a valid custom authoritative order.

Submit an invalid order.

Prove:

```text
rejected
coordinator value unchanged
persisted value unchanged
```

### J. Existing settings writes preserve custom tab order

Start with a custom tab order, then perform at least one existing coordinator setting mutation such as:

```text
ChangeLaunchAtWindowsStartup(...)
```

Reload and prove the custom tab order remains unchanged.

### K. No forbidden extra preference state

Regression guard that saved JSON does not introduce fields such as:

```text
DefaultOverlayTab
LastOverlayTab
SelectedOverlayTab
OverlayScrollOffset
```

Do not make this an exhaustive generic schema test; it only protects this PR's explicit non-goals.

---

## 14. Expected implementation scope

Likely files:

```text
src/SteamInputAddonforClaw.Contracts/Overlay/
    OverlayTabId.cs                         new shared identity + narrow order contract

src/SteamInputAddonforClaw/Settings/
    AppSettings.cs
    SettingsStore.cs
    StartupSettingsCoordinator.cs

src/SteamInputAddonforClaw.Overlay/
    OverlayTabState.cs                      consume shared identity/normalization only
    SteamInputAddonforClaw.Overlay.csproj   only if direct Contracts reference is required/desired

tests/SteamInputAddonforClaw.Tests/
    SettingsStoreTests.cs                   persistence coverage
    OverlayTabStateTests.cs                 shared identity import / existing behavior
    OverlayTabOrderContractTests.cs         optional focused shared invariant tests
```

Exact test-file split may differ.

Avoid unrelated source changes.

---

## 15. Explicit non-goals

Do not implement in OQ5-UI-08:

- OQ5-UI-09 `.Overlay` preference transport;
- OQ5-UI-10 Setting-page tab-order editor;
- `.Overlay` protocol bump;
- whole `AppSettings` serialization over IPC;
- direct settings.json reads from Overlay.exe;
- settings polling/file watching;
- separate `overlay-settings.json`;
- default-tab preference;
- last-tab restore;
- selected-row persistence;
- scroll persistence;
- Shortcut slot persistence;
- Device feature snapshot/mutations;
- TDP/CPU Boost/Power/FPS controls;
- slider delayed-commit changes;
- controller input changes;
- OQ4 changes;
- QAM visibility/process management;
- WING/OEM1 button assignment changes;
- generic settings schema/migration framework;
- generic reorderable collection framework.

---

## 16. Verification

Run at minimum:

```text
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test
git diff --check
```

Also inspect the produced test `settings.json` payload or equivalent assertions and prove:

```text
OverlayTabOrder uses enum names
all five tabs appear exactly once
no extra default/last/selected-tab preference was introduced
```

No MSI Claw hardware validation is required for this persistence-only PR beyond ensuring the full existing suite remains green. There is no controller/hardware behavior change to validate here.

---

## 17. Acceptance criteria

OQ5-UI-08 is complete when all of the following are true:

1. There is one shared fixed `OverlayTabId` identity for Runtime/Overlay use.
2. The five-tab order invariant has one narrow shared validation/default rule rather than duplicated algorithms.
3. `AppSettings` contains one `OverlayTabOrder` preference with the frozen default.
4. `SettingsStore` writes that order to the existing `settings.json` as enum names.
5. Missing or malformed persisted tab order falls back to default without discarding unrelated readable settings.
6. Runtime exposes a narrow validated tab-order mutation seam.
7. Invalid mutation requests are rejected and do not silently reset the user's valid order to default.
8. Existing settings mutations preserve the custom tab order.
9. Existing OverlayTabState default/custom/reset/traversal behavior remains intact.
10. No separate default-tab/last-tab/selected-row/scroll preference exists.
11. Overlay.exe does not read settings.json directly.
12. `.Overlay` remains protocol v4; no transport work is pulled forward from OQ5-UI-09.
13. No controller/Full1902/OQ4 lifecycle behavior changes.
14. Release build, full tests, and `git diff --check` pass.

---

## 18. Next PR boundary

After OQ5-UI-08 is merged, proceed to:

```text
OQ5-UI-09 — Overlay preference transport
```

That PR will be responsible for:

```text
Runtime authoritative OverlayTabOrder
→ .Overlay snapshot
→ OverlayTabState/order application

Overlay reorder request
→ narrow SetTabOrder-type transport mutation
→ Runtime validation/persistence through the OQ5-UI-08 seam
→ authoritative order returned/republished
```

Only OQ5-UI-09 should change the `.Overlay` protocol for this preference.