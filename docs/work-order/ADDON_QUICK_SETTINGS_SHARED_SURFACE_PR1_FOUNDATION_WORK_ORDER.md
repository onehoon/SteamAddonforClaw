# Work Order — Addon Quick Settings Shared Surface PR1 — Shared Shell Identity and Runtime Contract Foundation

**Date:** 2026-09-18  
**Baseline:** `main` at `4323c0df647a999cf17785b419fa4f1d030dff83`  
**Architecture authority:** `docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md`  
**Scope:** PR1 of the five-PR QAM / Windows Overlay frontend-convergence plan  
**Product:** Standalone Steam Addon for Claw / Full PID1902  
**Visible UI change:** None  
**Protocol target:** `FrontendTransportProtocol 30 -> 31`; `OverlayTransportProtocol` remains `7`

---

## 1. Read these authorities first

Before implementation, read and follow the current Full1902 authority documents in their documented precedence order:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`

Then read the frontend authorities:

- `docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md`
- `docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md`
- `docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md`
- `docs/QAM_SINGLE_ADDON_TAB_NATIVE_INNER_TABS_RESEARCH_AND_DESIGN_2026-09-13.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`
- `docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`
- `docs/work-order/QAM_NATIVE_01_STEAM_NATIVE_SURFACE_AND_INTERACTION_RECOVERY_WORK_ORDER.md`
- `docs/work-order/QAM_NATIVE_02_DEVICE_PROFILE_STABLE_TABS_WORK_ORDER.md`

Historical Overlay tab-order work is useful implementation context, but the new shared-surface architecture above is the current product direction:

- `docs/overlayui/OQ5_UI_08_RUNTIME_OWNED_OVERLAY_TAB_ORDER_SETTING_WORK_ORDER.md`
- `docs/overlayui/OQ5_UI_09_OVERLAY_PREFERENCE_TRANSPORT_WORK_ORDER.md`
- `docs/overlayui/OQ5_UI_10_SETTING_PAGE_TAB_ORDER_EDITOR_WORK_ORDER.md`

Do not restore any CTW integration or old temporary-routing architecture. This is standalone Full1902 work.

---

## 2. Goal

PR1 establishes one surface-neutral Addon Quick Settings shell identity and one read-only Runtime/frontend shell snapshot without changing what either surface visibly renders yet.

Current state:

```text
Overlay product identity
  OverlayTabId
    Device
    Profile
    Controller
    Shortcut
    Setting

QAM product identity
  qam.js currently owns only Device/Profile inner tabs
```

Target after PR1:

```text
Addon Quick Settings product identity
  AddonQuickSettingsTabId
    Device
    Profile
    Controller
    Shortcut
    Setting

one shared order contract
one shared label authority
one shared shell snapshot
one Runtime/frontend capture seam
one QAM bridge read operation

BUT

Overlay visible behavior = unchanged
QAM visible behavior     = unchanged
```

PR2 will make QAM actually render the five shared tabs.

PR1 only creates the correct single authority and transport foundation so PR2 cannot invent a second five-tab product definition.

---

## 3. Current production facts at the baseline

### 3.1 Current five-tab identity is incorrectly Overlay-named

Current source:

`src/SteamInputAddonforClaw.Contracts/Overlay/OverlayTabId.cs`

defines:

```csharp
public enum OverlayTabId
{
    Device,
    Profile,
    Controller,
    Shortcut,
    Setting,
}
```

and:

```csharp
public static class OverlayTabOrderContract
```

The semantics are no longer Overlay-only.

The exact same five identities and order will become the Addon Quick Settings product shell consumed by QAM and Overlay.

### 3.2 Current settings ownership is already Runtime-owned

Current `AppSettings` contains:

```csharp
public IReadOnlyList<OverlayTabId> OverlayTabOrder { get; init; }
    = OverlayTabOrderContract.DefaultOrder;
```

Current `StartupSettingsCoordinator` owns the mutation:

```csharp
public bool TryChangeOverlayTabOrder(IReadOnlyList<OverlayTabId> requested)
```

Keep that one Runtime settings authority.

Do not create a QAM-specific order or second settings property.

### 3.3 Current persisted JSON key must be preserved

Current `SettingsStore` reads/writes:

```json
"OverlayTabOrder": [
  "Device",
  "Profile",
  "Controller",
  "Shortcut",
  "Setting"
]
```

This PR changes the C# product naming, not the user's persisted settings schema.

Keep the disk key exactly:

```text
OverlayTabOrder
```

Do not add a second `AddonQuickSettingsTabOrder` JSON key.

Do not add a migration framework.

### 3.4 Current Overlay transport already carries the order

Current:

`src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`

uses:

```csharp
IReadOnlyList<OverlayTabId>? TabOrder
```

and:

```text
TabOrderState
SetTabOrder
```

Current protocol:

```text
OverlayTransportProtocol.CurrentVersion = 7
```

This transport already has the correct semantics for the Windows Overlay.

PR1 must only change the in-process type identity from Overlay-specific to Addon-Quick-Settings-specific.

The serialized member name and enum names remain unchanged.

Therefore:

```text
OverlayTransportProtocol.CurrentVersion remains 7
```

### 3.5 Current frontend/QAM transport is v30

Current:

`src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs`

has:

```csharp
public static class FrontendTransportProtocol
{
    public const int CurrentVersion = 30;
}
```

PR1 adds a new frontend RPC that an old v30 peer does not know.

Therefore PR1 must bump:

```text
FrontendTransportProtocol.CurrentVersion
30 -> 31
```

### 3.6 QAM bridge currently exposes only status + shared Device/Profile operations

Current `QamFrontendBridge` allowlist is effectively:

```text
captureStatus
captureQuickSettingsPage
mutateQuickSetting
```

PR1 adds one read-only operation:

```text
captureQuickSettingsShell
```

Do not change `qam.js` to call it in this PR.

PR2 will consume it.

---

## 4. Required architectural result

After PR1, the ownership graph must be:

```text
StartupSettingsCoordinator
        │
        │ one current persisted tab order
        ▼
AddonQuickSettingsTabOrder
        │
        ├───────────────┐
        │               │
        ▼               ▼
Overlay transport    IAddonFrontendControl
TabOrderState        CaptureAddonQuickSettingsShellAsync
        │               │
        ▼               ▼
Overlay.exe          .Frontend/.Qam
(existing UI)            │
                         ▼
                  QamFrontendBridge
                  captureQuickSettingsShell
                  (not used by qam.js yet)
```

There must not be:

- a QAM-specific order;
- a QAM-specific label array;
- a second persisted setting;
- a second Runtime owner;
- a new manager/service;
- a generic page registry.

---

## 5. Promote the five-tab contract to the shared frontend contract namespace

### 5.1 Preferred source location

Move the product contract out of the Overlay-specific namespace.

Preferred new file:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs
```

Preferred namespace:

```csharp
namespace SteamInputAddonforClaw.Contracts.Frontend;
```

Delete the old product-identity source once all call sites are migrated:

```text
src/SteamInputAddonforClaw.Contracts/Overlay/OverlayTabId.cs
```

Do not leave compatibility aliases such as:

```csharp
using OverlayTabId = AddonQuickSettingsTabId;
```

or a duplicate obsolete enum/class.

The repository is pre-release and all product binaries ship together. A duplicate alias would create two names for one authority and undermine this PR.

### 5.2 New enum

Use a surface-neutral identity:

```csharp
public enum AddonQuickSettingsTabId
{
    Device,
    Profile,
    Controller,
    Shortcut,
    Setting,
}
```

Do not change member names or order in this PR.

Keeping exact member names preserves current string-enum wire and persisted values.

### 5.3 Rename the order contract

Conceptually:

```csharp
public static class AddonQuickSettingsTabOrderContract
{
    private static readonly AddonQuickSettingsTabId[] Default =
    [
        AddonQuickSettingsTabId.Device,
        AddonQuickSettingsTabId.Profile,
        AddonQuickSettingsTabId.Controller,
        AddonQuickSettingsTabId.Shortcut,
        AddonQuickSettingsTabId.Setting,
    ];

    public static IReadOnlyList<AddonQuickSettingsTabId> DefaultOrder
        => (AddonQuickSettingsTabId[])Default.Clone();

    public static bool TryNormalize(
        IReadOnlyList<AddonQuickSettingsTabId>? requested,
        out IReadOnlyList<AddonQuickSettingsTabId> normalized)
    {
        if (requested is null || requested.Count != Default.Length)
        {
            normalized = DefaultOrder;
            return false;
        }

        var seen = new HashSet<AddonQuickSettingsTabId>();
        foreach (var id in requested)
        {
            if (!Enum.IsDefined(id) || !seen.Add(id))
            {
                normalized = DefaultOrder;
                return false;
            }
        }

        normalized = requested.ToArray();
        return true;
    }

    public static IReadOnlyList<AddonQuickSettingsTabId> NormalizeOrDefault(
        IReadOnlyList<AddonQuickSettingsTabId>? requested)
    {
        TryNormalize(requested, out var normalized);
        return normalized;
    }
}
```

This is the existing invariant under a correct product name.

Do not redesign the validation algorithm.

---

## 6. Add the narrow shared shell snapshot

PR2 needs QAM to obtain the same ordered tabs and labels the Overlay product uses.

Add the smallest closed typed contract.

Recommended shape:

```csharp
public sealed record AddonQuickSettingsTabSnapshot(
    AddonQuickSettingsTabId TabId,
    string Label);

public sealed record AddonQuickSettingsShellSnapshot(
    bool Available,
    string? Message,
    IReadOnlyList<AddonQuickSettingsTabSnapshot> Tabs)
{
    public static AddonQuickSettingsShellSnapshot Unavailable(
        string? message = null)
        => new(
            false,
            message ?? "Addon Quick Settings are unavailable.",
            []);
}
```

Do not add:

- renderer type;
- icon type;
- React component name;
- XAML control name;
- per-surface visibility;
- selected tab;
- current focus;
- navigation state;
- page payloads;
- arbitrary metadata dictionary.

This snapshot answers only:

> What are the five Addon Quick Settings tabs, in what current product order, and what product label does each have?

---

## 7. One shared label authority

Current Overlay code contains a local method like:

```csharp
private static string LabelFor(OverlayTabId id) => id switch
{
    OverlayTabId.Device => "Device",
    OverlayTabId.Profile => "Profile",
    ...
};
```

That must stop being an Overlay product authority.

Add one narrow shared label mapping adjacent to the shell contract.

For example:

```csharp
public static class AddonQuickSettingsShellContract
{
    public static string LabelFor(AddonQuickSettingsTabId tab) => tab switch
    {
        AddonQuickSettingsTabId.Device => "Device",
        AddonQuickSettingsTabId.Profile => "Profile",
        AddonQuickSettingsTabId.Controller => "Controller",
        AddonQuickSettingsTabId.Shortcut => "Shortcut",
        AddonQuickSettingsTabId.Setting => "Setting",
        _ => throw new ArgumentOutOfRangeException(nameof(tab)),
    };

    public static AddonQuickSettingsShellSnapshot Create(
        IReadOnlyList<AddonQuickSettingsTabId>? requestedOrder)
    {
        var order = AddonQuickSettingsTabOrderContract.NormalizeOrDefault(requestedOrder);

        return new AddonQuickSettingsShellSnapshot(
            Available: true,
            Message: null,
            Tabs: order
                .Select(id => new AddonQuickSettingsTabSnapshot(id, LabelFor(id)))
                .ToArray());
    }
}
```

Exact class naming may vary if there is a clearer repository-consistent name.

Required invariant:

```text
tab identity/order/label product facts
→ defined once in shared contract code
```

Do not add localization infrastructure in this PR.

---

## 8. Rename the C# settings authority, but preserve the disk key

### 8.1 AppSettings

Rename the in-memory product property conceptually:

```csharp
public IReadOnlyList<AddonQuickSettingsTabId> AddonQuickSettingsTabOrder { get; init; }
    = AddonQuickSettingsTabOrderContract.DefaultOrder;
```

Do not keep a second `OverlayTabOrder` C# property as an alias.

### 8.2 SettingsStore load

Rename the helper to reflect the product identity if useful:

```csharp
private static IReadOnlyList<AddonQuickSettingsTabId>
    ReadAddonQuickSettingsTabOrder(JsonElement root)
```

but continue reading the exact old/current disk member:

```csharp
root.TryGetProperty("OverlayTabOrder", out var property)
```

Example:

```csharp
private static IReadOnlyList<AddonQuickSettingsTabId>
    ReadAddonQuickSettingsTabOrder(JsonElement root)
{
    if (!root.TryGetProperty("OverlayTabOrder", out var property)
        || property.ValueKind != JsonValueKind.Array)
    {
        return AddonQuickSettingsTabOrderContract.DefaultOrder;
    }

    var parsed =
        new List<AddonQuickSettingsTabId>(property.GetArrayLength());

    foreach (var element in property.EnumerateArray())
    {
        var name =
            element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;

        if (string.IsNullOrEmpty(name)
            || !char.IsLetter(name[0])
            || !Enum.TryParse<AddonQuickSettingsTabId>(
                name,
                ignoreCase: false,
                out var tab))
        {
            return AddonQuickSettingsTabOrderContract.DefaultOrder;
        }

        parsed.Add(tab);
    }

    if (AddonQuickSettingsTabOrderContract.TryNormalize(
            parsed,
            out var normalized))
    {
        return normalized;
    }

    return normalized; // invalid shape already resolves to the frozen default
}
```

Keep the current feature-local warning/log behavior. The abbreviated sample above is about the type/key relationship, not permission to remove existing logs.

### 8.3 SettingsStore save

Continue writing:

```text
OverlayTabOrder
```

Example:

```csharp
var payload = new
{
    LogLevel = settings.LogLevel.ToString(),
    settings.SuppressDeveloperMenuWarning,
    settings.DeveloperMenuEnabled,
    settings.FrontButtonMapping,

    // Deliberately preserve the existing persisted JSON member name.
    OverlayTabOrder =
        AddonQuickSettingsTabOrderContract.NormalizeOrDefault(
            settings.AddonQuickSettingsTabOrder),
};
```

Do not write both old and new keys.

Do not rename the persisted property in this PR.

---

## 9. Rename the Runtime settings seam

Update `StartupSettingsCoordinator` to surface-neutral product naming.

Conceptually:

```csharp
public IReadOnlyList<AddonQuickSettingsTabId> AddonQuickSettingsTabOrder
    => Settings.AddonQuickSettingsTabOrder;

public bool TryChangeAddonQuickSettingsTabOrder(
    IReadOnlyList<AddonQuickSettingsTabId> requested)
{
    if (!AddonQuickSettingsTabOrderContract.TryNormalize(
            requested,
            out var normalized))
    {
        return false;
    }

    if (normalized.SequenceEqual(
            Settings.AddonQuickSettingsTabOrder))
    {
        return true;
    }

    var next = Settings with
    {
        AddonQuickSettingsTabOrder = normalized
    };

    _settingsStore.Save(next);
    Settings = next;
    return true;
}
```

Preserve current semantics:

```text
invalid candidate
→ reject
→ no write
→ keep current order

same valid candidate
→ accepted no-op

new valid candidate
→ save first
→ publish as current state
```

Do not add an event bus in PR1 solely for future live cross-surface synchronization.

PR3 can add the smallest notification needed when Setting becomes a true dual-surface mutation path.

---

## 10. Migrate Overlay code to the shared identity with no visual change

Update all Overlay-side consumers from:

```text
OverlayTabId
OverlayTabOrderContract
OverlayTabOrder
TryChangeOverlayTabOrder
```

to the new shared product identity/seams.

Likely production files include, after confirming every current call site:

- `src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs`
- `src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs`
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`
- `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`
- `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`

Do not rename surface-local classes merely because their parameter type changed.

For example, these names can remain:

```text
OverlayTabState
OverlayTabOrderRow
OverlayWindow
```

They really are Overlay renderer state/classes.

Only the product identity/order authority needs surface-neutral naming.

### Overlay label usage

Replace the local Overlay `LabelFor(...)` product mapping with the shared label authority.

Conceptually:

```csharp
var button = new Button
{
    Content = AddonQuickSettingsShellContract.LabelFor(id),
    Tag = id,
    ...
};
```

and:

```csharp
var row = new OverlayTabOrderRow(
    id,
    AddonQuickSettingsShellContract.LabelFor(id),
    delta => RequestTabOrderMove(id, delta));
```

There must be no second hard-coded five-label switch left in Overlay production code.

---

## 11. Overlay transport type migration — no protocol bump

Change the in-memory wire type:

```csharp
internal sealed record OverlayWireMessage(
    int ProtocolVersion,
    OverlayWireMessageKind Kind,
    ...
    IReadOnlyList<AddonQuickSettingsTabId>? TabOrder = null,
    ...);
```

Likewise update:

- server get/set delegates;
- client callbacks;
- `ValidateTabOrderMessage`;
- `SendSetTabOrderAsync`;
- tests.

### Why v7 remains valid

The serialized JSON shape remains:

```json
{
  "TabOrder": [
    "Device",
    "Profile",
    "Controller",
    "Shortcut",
    "Setting"
  ]
}
```

The property name is unchanged.

The enum member strings are unchanged.

No message kind is added/removed/reinterpreted.

Therefore:

```csharp
internal const int CurrentVersion = 7;
```

must remain unchanged.

Do not bump Overlay protocol merely because a C# type was renamed.

---

## 12. Add the Runtime shell capture seam

Extend `IAddonFrontendControl` with one read-only method.

Recommended shape:

```csharp
Task<AddonQuickSettingsShellSnapshot>
    CaptureAddonQuickSettingsShellAsync(
        CancellationToken cancellationToken = default)
    => Task.FromResult(
        AddonQuickSettingsShellSnapshot.Unavailable());
```

Use a fail-closed default.

Do not fabricate the default five tabs for arbitrary interface implementations that did not opt into this feature.

### InProcessAddonFrontendControl

Implement it from the existing one Runtime settings authority:

```csharp
public Task<AddonQuickSettingsShellSnapshot>
    CaptureAddonQuickSettingsShellAsync(
        CancellationToken cancellationToken = default)
{
    ThrowIfShuttingDown();

    return Task.FromResult(
        AddonQuickSettingsShellContract.Create(
            _settings.AddonQuickSettingsTabOrder));
}
```

This method:

- reads no hardware;
- performs no I/O;
- owns no state;
- changes no settings;
- emits no controller action;
- does not inspect Steam;
- does not depend on QAM or Overlay visibility.

Do not create a `QuickSettingsShellManager`.

---

## 13. Add the frontend RPC — v30 to v31

### 13.1 Protocol comment/version

Append a Version 31 comment to `FrontendWire.cs`.

Example:

```csharp
// Version 31: Addon Quick Settings shared-surface PR1 adds the read-only
// CaptureAddonQuickSettingsShell RPC carrying AddonQuickSettingsShellSnapshot
// so QAM can consume the same five-tab identity/order/labels already owned by
// Runtime settings / Overlay. A v30 peer does not know this RPC, so fail the
// handshake up front. Overlay protocol is unchanged.
public static class FrontendTransportProtocol
{
    public const int CurrentVersion = 31;
}
```

### 13.2 Add one RPC method

Add:

```csharp
CaptureAddonQuickSettingsShell
```

to `FrontendRpcMethod`.

This is a no-payload capture operation.

Do not add a request record.

### 13.3 Server validation

Treat this method like the other no-payload captures.

A request carrying an unexpected payload must fail with the existing `InvalidMessage` behavior and must not invoke the inner control.

### 13.4 Server dispatch

Conceptually:

```csharp
m == FrontendRpcMethod.CaptureAddonQuickSettingsShell
    ? FrontendWireCodec.Payload(
        await _inner
            .CaptureAddonQuickSettingsShellAsync(t)
            .ConfigureAwait(false))
```

Keep the existing transport style.

Do not introduce a generic capture dispatcher.

### 13.5 Client method

Add:

```csharp
public Task<AddonQuickSettingsShellSnapshot>
    CaptureAddonQuickSettingsShellAsync(
        CancellationToken t = default)
    => SendAsync<AddonQuickSettingsShellSnapshot>(
        FrontendRpcMethod.CaptureAddonQuickSettingsShell,
        null,
        t);
```

No new client-side cache.

---

## 14. Add the QAM bridge read operation, but do not change qam.js

Add one bridge allowlist method:

```text
captureQuickSettingsShell
```

Conceptually:

```csharp
object result = method switch
{
    "captureStatus"
        => await _client.CaptureStatusAsync(token),

    "captureQuickSettingsShell"
        => await _client
            .CaptureAddonQuickSettingsShellAsync(token),

    "captureQuickSettingsPage"
        => await CaptureQuickSettingsPageAsync(root, token),

    "mutateQuickSetting"
        => await MutateQuickSettingAsync(root, token),

    _ => throw new InvalidOperationException(
        "Unsupported QAM method.")
};
```

This operation is read-only.

It needs no QAM-specific Steam/BPM admission check.

Do not modify:

```text
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
```

in PR1.

The new bridge method intentionally has no production JS caller until PR2.

This is acceptable foundation work because PR2 then only changes the renderer and does not need to also redesign the transport.

---

## 15. Do not extend QuickSettingsPageId

Do **not** add:

```text
Controller
Shortcut
Setting
```

to:

```csharp
QuickSettingsPageId
```

PR1 is a shell contract PR.

The existing `QuickSettingsPageSnapshot` remains exactly the Device/Profile shared Toggle/Slider product contract.

The higher-level relationship is:

```text
AddonQuickSettingsShellSnapshot
  Device      -> QuickSettingsPageId.Device
  Profile     -> QuickSettingsPageId.Profile
  Controller  -> no typed content contract yet
  Shortcut    -> dedicated contract in PR4
  Setting     -> dedicated contract in PR3
```

Do not distort the existing Device/Profile model to make all five tabs look structurally identical.

---

## 16. Do not add shared selected-tab state

Shared product facts:

- IDs;
- order;
- labels.

Surface-local transient facts:

- selected tab;
- focused control;
- scroll position;
- Steam navigation stack;
- Overlay row-selection index.

Therefore do not add:

```text
SelectedTab
ActiveTab
LastTab
FocusedTab
```

to the Runtime shell snapshot or settings.

QAM and Overlay are allowed to select different initial tabs according to their own existing surface policy.

---

## 17. Do not add live cross-surface synchronization yet

PR1 creates the read seam.

It does not need a new event solely so an already-open QAM immediately reacts when Overlay changes order.

Do not add:

- `AddonQuickSettingsShellChanged` manager;
- settings observer service;
- selected-tab synchronization;
- extra polling;
- periodic shell capture.

PR2 and PR3 will define the actual QAM renderer / Setting mutation behavior and can add the smallest event-driven refresh needed from real usage.

Avoid speculative infrastructure in PR1.

---

## 18. Required tests

Update existing tests for the type/seam rename and add focused tests for the new contract.

### 18.1 Shared shell contract tests

Preferred new test file:

```text
tests/SteamInputAddonforClaw.Tests/AddonQuickSettingsShellContractTests.cs
```

Cover at minimum:

1. default order is exactly:

```text
Device
Profile
Controller
Shortcut
Setting
```

2. `DefaultOrder` returns an independent copy;

3. complete custom order normalizes unchanged;

4. duplicate tab rejects;

5. missing tab rejects;

6. unknown enum value rejects;

7. null rejects/falls back according to existing contract semantics;

8. labels are exactly:

```text
Device
Profile
Controller
Shortcut
Setting
```

9. `Create(...)` preserves requested normalized order and matches each label to the correct identity;

10. unavailable snapshot has no editable/product content and is distinguishable from an available shell.

### 18.2 SettingsStore regression tests

Update current Overlay-tab tests to shared product naming, but preserve disk-format coverage.

Mandatory regression:

A pre-PR1 settings file containing:

```json
{
  "OverlayTabOrder": [
    "Controller",
    "Device",
    "Profile",
    "Shortcut",
    "Setting"
  ]
}
```

must load into:

```text
AppSettings.AddonQuickSettingsTabOrder
```

with the exact custom order.

A subsequent normal `Save` must still write:

```text
OverlayTabOrder
```

and must not write:

```text
AddonQuickSettingsTabOrder
```

Malformed tab-order data must still fall back for this feature only without resetting unrelated readable settings.

### 18.3 StartupSettingsCoordinator tests

Update/rename the current order tests to prove:

- invalid reorder rejected;
- valid reorder persisted;
- same order accepted as no-op;
- save failure does not publish a new in-memory order if current behavior already guarantees that;
- current order remains one Runtime authority.

### 18.4 Overlay tab-state/editor tests

Update existing:

- `OverlayTabStateTests`;
- `OverlayTabOrderContractTests` (rename to shared product naming if appropriate);
- `OverlayTabOrderEditorTests`;
- `OverlayTabOrderTransportTests`;
- Quick Settings Overlay transport tests that mention the old enum type.

Visible behavior must remain unchanged.

### 18.5 Overlay wire serialization test

Add/retain an explicit assertion that the v7 `TabOrder` wire still serializes enum names, not numbers.

Expected names:

```json
["Device","Profile","Controller","Shortcut","Setting"]
```

Assert:

```text
OverlayTransportProtocol.CurrentVersion == 7
```

This guards against accidentally treating the C# type rename as a wire redesign.

### 18.6 In-process shell seam test

Add focused coverage, preferably beside the existing `QuickSettingsInProcessSeamTests` or in a small dedicated shell test.

Prove:

```text
StartupSettingsCoordinator current custom order
        ↓
InProcessAddonFrontendControl.CaptureAddonQuickSettingsShellAsync
        ↓
same IDs in same order
+ canonical shared labels
```

No hardware dependency should be needed.

### 18.7 Frontend named-pipe transport test

Update current protocol assertion:

```csharp
Assert.Equal(31, FrontendTransportProtocol.CurrentVersion);
```

Add a shell round-trip test:

```csharp
var shell =
    await client.CaptureAddonQuickSettingsShellAsync();

Assert.Equivalent(fake.ShellSnapshot, shell, strict: true);
Assert.Equal(1, fake.CaptureShellCount);
```

Add a raw-frame negative test proving an unexpected payload on the no-payload shell capture is rejected without invoking the frontend.

### 18.8 QamFrontendBridge tests

Add:

```text
captureQuickSettingsShell
→ bridge
→ NamedPipeAddonFrontendClient
→ Runtime fake
→ AddonQuickSettingsShellSnapshot
```

Prove:

- read call succeeds;
- no Steam/BPM admission is required;
- unknown bridge method still fails;
- shell capture does not mutate anything.

No qam.js renderer assertions are required in PR1.

---

## 19. Required grep / duplicate-removal checks

Before completion, search production source for:

```text
OverlayTabId
OverlayTabOrderContract
TryChangeOverlayTabOrder
Settings.OverlayTabOrder
LabelFor(OverlayTabId
```

Expected result after migration:

- no obsolete product-authority type/class remains;
- no duplicate hard-coded five-label Overlay switch remains;
- historical docs/comments/tests may still contain old names only where they intentionally describe history;
- current production paths use the shared Addon Quick Settings names.

Do not blindly rewrite historical work-order documents.

---

## 20. Likely production file set

Confirm against latest source immediately before editing.

Expected main production areas:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/
    AddonQuickSettingsShellContracts.cs                 [new]

src/SteamInputAddonforClaw.Contracts/Overlay/
    OverlayTabId.cs                                     [remove after migration]

src/SteamInputAddonforClaw.Contracts/Frontend/
    FrontendContracts.cs

src/SteamInputAddonforClaw/Settings/
    AppSettings.cs
    SettingsStore.cs
    StartupSettingsCoordinator.cs

src/SteamInputAddonforClaw/Frontend/
    InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.FrontendTransport/
    FrontendWire.cs
    NamedPipeAddonFrontendClient.cs
    NamedPipeAddonFrontendServer.cs
    OverlayWire.cs

src/SteamInputAddonforClaw.QamHost/
    QamFrontendBridge.cs

src/SteamInputAddonforClaw.Overlay/
    App.xaml.cs
    OverlayTabState.cs
    OverlayTabOrderRow.cs
    OverlayWindow.xaml.cs

src/SteamInputAddonforClaw/Lifecycle/
    OverlayProcessController.cs

src/SteamInputAddonforClaw/Hosting/
    AddonProcessHost.cs
```

Do not modify `qam.js` in this PR.

No csproj change should be required if the projects retain their current SDK default compile-item behavior and existing contract references.

---

## 21. Explicit non-goals

Do not implement any PR2+ work early.

Specifically, do not:

- render five tabs in QAM yet;
- change current QAM Device/Profile visible layout;
- add Controller content;
- add Shortcut functionality;
- add QAM Setting editor;
- expose tab-order mutation through .Qam yet;
- change QAM initial-tab policy;
- add shared selected-tab state;
- add a WebView2 frontend;
- replace Steam native controls;
- replace WinUI Overlay controls;
- merge QamHost and Overlay processes;
- merge .Qam and .Overlay pipes;
- add a UI plugin framework;
- add a generic form/page schema;
- add reflection dispatch;
- add a new state cache;
- add polling;
- change OQ4 controller capture;
- change PID1901/PID1902;
- change HidHide;
- change VIIPER;
- change X360/SteamDeck presentation policy;
- change sleep/resume/restart/device-loss lifecycle.

---

## 22. Full1902 / lifecycle invariants

PR1 is frontend contract work only.

The following must remain behaviorally untouched:

```text
Center M Enabled / Disabled authority
PID1901 / PID1902 ownership
DirectInput ownership
HidHide ownership/baseline
VIIPER server/bus ownership
Xbox360 / SteamDeck presentation selection
Steam/BPM detection
PnP loss / re-enumeration recovery
Sleep / Hibernate / Resume
Restart / Shutdown teardown
OQ4 capture / neutral output / release gate
front-button action dispatch
```

No shell/tab state is controller authority.

---

## 23. Implementation sequence

Recommended implementation order inside the PR:

### Step A — shared contract

1. Add `AddonQuickSettingsShellContracts.cs`.
2. Add `AddonQuickSettingsTabId`.
3. Add `AddonQuickSettingsTabOrderContract`.
4. Add shell tab/snapshot records.
5. Add shared label/snapshot factory.
6. Add focused contract tests.

### Step B — migrate current Overlay/settings users

1. Rename AppSettings in-memory property.
2. Preserve disk key `OverlayTabOrder`.
3. Rename StartupSettingsCoordinator read/mutation seam.
4. Migrate Overlay state/window/editor/transport types.
5. Remove old `OverlayTabId.cs`.
6. Run focused Overlay/settings tests.

### Step C — add Runtime/frontend capture

1. Add interface method.
2. Implement in `InProcessAddonFrontendControl`.
3. Add in-process seam test.

### Step D — add frontend/QAM transport

1. Bump frontend protocol 30 -> 31.
2. Add `CaptureAddonQuickSettingsShell` RPC.
3. Add client/server dispatch.
4. Add `captureQuickSettingsShell` bridge method.
5. Keep `qam.js` untouched.
6. Add transport/bridge tests.

### Step E — full regression

Run the complete test suite.

Do not move on to PR2 inside this branch.

---

## 24. Build and test requirements

At minimum:

```powershell
dotnet build
dotnet test
```

Use the repository's normal solution/project entry points.

Also run any existing publish/asset validation only if the ordinary CI currently runs it for code changes of this kind.

No hardware validation is required for PR1 because there is no visible or controller-lifecycle behavior change.

A quick manual smoke check is still useful if convenient:

```text
launch Runtime
open Windows Overlay
five tabs look exactly as before
existing custom tab order still loads
Setting reorder still works
close/reopen Overlay preserves order
QAM still shows its existing Device/Profile UI exactly as before
```

Do not treat lack of hardware smoke testing as a blocker if all focused/full tests pass; this PR does not change hardware/controller behavior.

---

## 25. Acceptance checklist

### Shared identity

```text
[ ] AddonQuickSettingsTabId is the one current five-tab product identity
[ ] AddonQuickSettingsTabOrderContract is the one current order validator/default
[ ] old OverlayTabId product enum is removed
[ ] old OverlayTabOrderContract product class is removed
[ ] no compatibility alias creates a second authority
```

### Shared labels/snapshot

```text
[ ] five tab labels are defined once
[ ] AddonQuickSettingsShellSnapshot is closed and typed
[ ] shell snapshot contains ordered identity + label only
[ ] no selected/focused/rendering state was added
```

### Settings

```text
[ ] AppSettings uses surface-neutral C# naming
[ ] StartupSettingsCoordinator uses surface-neutral C# naming
[ ] persisted JSON key remains exactly OverlayTabOrder
[ ] existing custom persisted order still round-trips
[ ] no second settings key/property authority exists
```

### Overlay

```text
[ ] Overlay visible behavior is unchanged
[ ] Overlay consumes shared identity/order/labels
[ ] local duplicate LabelFor five-tab switch is gone
[ ] Overlay wire still uses TabOrderState / SetTabOrder
[ ] OverlayTransportProtocol remains 7
```

### Frontend/QAM foundation

```text
[ ] IAddonFrontendControl exposes read-only shell capture
[ ] InProcessAddonFrontendControl projects current Runtime settings
[ ] FrontendTransportProtocol is 31
[ ] CaptureAddonQuickSettingsShell RPC exists
[ ] unexpected RPC payload is rejected
[ ] NamedPipeAddonFrontendClient round-trips the shell snapshot
[ ] QamFrontendBridge exposes captureQuickSettingsShell
[ ] qam.js is unchanged
```

### Safety / scope

```text
[ ] no controller authority change
[ ] no OQ4 change
[ ] no new polling
[ ] no new manager/service
[ ] no generic UI framework
[ ] no Controller/Shortcut/Setting feature implementation
[ ] no QAM five-tab rendering yet
[ ] full test suite passes
```

---

## 26. Review standard

Treat as blocking only realistic defects that affect the current supported product, for example:

- existing persisted `OverlayTabOrder` no longer loads;
- save writes a new key and silently loses the existing user order;
- Overlay order/labels visibly change;
- duplicate five-tab authorities remain in production;
- malformed order becomes accepted;
- Frontend v30 peers can connect despite the new RPC contract;
- Overlay protocol is unnecessarily bumped or wire shape accidentally changes;
- shell capture returns a different order than Runtime settings;
- QAM bridge shell capture bypasses the one Runtime source;
- qam.js is changed and introduces premature PR2 behavior;
- Full1902/controller lifecycle is touched by this frontend-only PR.

Do not block for theoretical instruction-level races unrelated to the supported lifecycle.

Do not add synchronization/state/abstractions merely to defend against artificial interleavings.

---

## 27. Final required outcome

PR1 is complete when the repository has this architecture:

```text
                  Runtime settings
                        │
          AddonQuickSettingsTabOrder
                        │
        ┌───────────────┴────────────────┐
        │                                │
        ▼                                ▼
AddonQuickSettingsShellContract      .Overlay v7
ID / order / labels                  existing TabOrderState
        │                                │
        ▼                                ▼
IAddonFrontendControl                  Overlay.exe
CaptureAddonQuickSettingsShellAsync     same visible UI
        │
        ▼
.Frontend / .Qam v31
        │
        ▼
QamFrontendBridge
captureQuickSettingsShell
        │
        └── qam.js does NOT consume it until PR2
```

The product rule after PR1 is:

> **There is one five-tab Addon Quick Settings identity/order/label authority. Overlay already consumes it; QAM has the read seam ready to consume it next. No visible frontend behavior changes in PR1.**
