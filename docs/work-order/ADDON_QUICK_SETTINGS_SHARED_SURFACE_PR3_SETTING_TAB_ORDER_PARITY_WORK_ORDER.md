# Work Order — Addon Quick Settings Shared Surface PR3: Shared Setting Tab-Order Product + QAM/Overlay Parity

> **Date:** 2026-09-19  
> **Status:** Ready for implementation  
> **Reviewed production baseline:** `main` at `fe4677fa08d000abdf03367812044d4b59bb6a8f` after PR #528  
> **Previous shared-surface PRs:** PR #527, PR #528  
> **Architecture authority:** `docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md`  
> **Full1902 authority:** `docs/Full 1902 Implementation/README.md` and referenced Full1902 documents  
> **Product scope:** Standalone Full PID1902  
> **CTW integration:** Out of scope  
> **Main goal:** Move the Setting / Tab Order product semantics out of the Overlay renderer, give QAM and Overlay the same closed typed state/mutation contract, and preserve two surface-specific renderers.

---

## 1. Goal

PR1 established one shared five-tab identity/order authority.

PR2 changed QAM from two Addon top-level tabs into:

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

At the reviewed PR #528 baseline, Setting is still asymmetric:

```text
Overlay Setting
→ functional five-row tab-order editor
→ Overlay-local proposal logic
→ .Overlay SetTabOrder whole-order request
→ StartupSettingsCoordinator

QAM Setting
→ placeholder only
```

PR3 must converge that into:

```text
                    Runtime settings authority
                             │
                             ▼
             shared Setting tab-order product
          state + one-position move mutation meaning
                  │                    │
          ┌───────┘                    └───────┐
          ▼                                    ▼
      Steam QAM                            Overlay
 native Setting renderer              WinUI Setting renderer
```

The Runtime remains the only persistence authority.

The two renderers may look different. They must have the same:

- five row identities;
- row order;
- row labels;
- one-position move semantics;
- boundary semantics;
- authoritative mutation readback;
- failure meaning.

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

docs/overlayui/OQ5_UI_09_OVERLAY_PREFERENCE_TRANSPORT_WORK_ORDER.md
docs/overlayui/OQ5_UI_10_SETTING_PAGE_TAB_ORDER_EDITOR_WORK_ORDER.md
```

Then inspect the current production source:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsShellContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs

src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs

src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs

src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js

src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabState.cs
src/SteamInputAddonforClaw.Overlay/OverlayTabOrderRow.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.xaml.cs
```

Relevant current tests:

```text
tests/SteamInputAddonforClaw.Tests/OverlayTabOrderContractTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayTabOrderEditorTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayTabOrderTransportTests.cs
tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs
tests/SteamInputAddonforClaw.Tests/QuickSettingsInProcessSeamTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
```

Do not use CTW integration as an implementation target.

---

## 3. Current production facts at `fe4677f...`

### 3.1 The persistent authority is already surface-neutral

Current:

```csharp
public IReadOnlyList<AddonQuickSettingsTabId> AddonQuickSettingsTabOrder { get; init; }
    = AddonQuickSettingsTabOrderContract.DefaultOrder;
```

and:

```csharp
public bool TryChangeAddonQuickSettingsTabOrder(
    IReadOnlyList<AddonQuickSettingsTabId> requested)
```

already live in Runtime settings.

Do not create another persisted Setting owner.

### 3.2 The disk key must remain compatible

`SettingsStore` deliberately still persists the historical JSON property:

```text
"OverlayTabOrder"
```

PR3 does not rename this disk key.

Do not create a migration framework. Do not write two keys.

### 3.3 Overlay still owns the move proposal algorithm

Current `OverlayTabState.TryCreateMovedOrder(...)`:

- knows the current five-item order;
- validates delta `-1 / +1`;
- computes the moved whole-order proposal;
- rejects boundaries locally.

That is product behavior and should no longer be Overlay-specific after PR3.

### 3.4 Overlay transport still sends a whole order

Current:

```text
OverlayWindow
→ TabOrderChangeRequested(proposed whole order)
→ App
→ NamedPipeOverlayClient.SendSetTabOrderAsync(order)
→ .Overlay SetTabOrder
→ Runtime TryChangeAddonQuickSettingsTabOrder(order)
→ TabOrderState readback
```

This is functional, but it is not yet the shared Setting mutation contract.

### 3.5 QAM already owns the five-tab shell but Setting is a placeholder

Current PR #528 `qam.js` maps:

```text
Device     -> QuickSettingsPanel(Device)
Profile    -> QuickSettingsPanel(Profile)
Controller -> placeholder
Shortcut   -> placeholder
Setting    -> placeholder
```

PR3 replaces only the Setting placeholder.

Controller and Shortcut remain unchanged in this PR.

### 3.6 QAM already has the native control needed for a focused PR

Current native resolver already has:

```text
SliderField
ToggleField
PanelSection
PanelSectionRow
Tabs
```

Do not add another Steam component resolver just to get arrow buttons.

For this PR, use the existing native `SliderField` as the QAM row's **position control**.

That gives:

- exactly five product rows;
- native Steam focus/navigation;
- bounded left/right movement;
- no custom HTML control;
- no new CommonUI export fingerprint;
- no additional Steam-version-sensitive resolver.

---

## 4. Product contract decision

Add the smallest dedicated typed Setting contract.

Preferred new source:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/AddonQuickSettingsTabOrderContracts.cs
```

Keep the existing:

```text
AddonQuickSettingsTabId
AddonQuickSettingsTabOrderContract
AddonQuickSettingsShellSnapshot
AddonQuickSettingsShellContract
```

where they are unless a very small file move materially improves clarity.

Do not rename the shared shell contract again.

### 4.1 State

Add:

```csharp
public sealed record AddonQuickSettingsTabOrderRow(
    AddonQuickSettingsTabId TabId,
    string Label,
    bool CanMoveEarlier,
    bool CanMoveLater);

public sealed record AddonQuickSettingsTabOrderSnapshot(
    bool Available,
    IReadOnlyList<AddonQuickSettingsTabOrderRow> Rows)
{
    public static AddonQuickSettingsTabOrderSnapshot Unavailable() =>
        new(false, Array.Empty<AddonQuickSettingsTabOrderRow>());
}
```

Rows are already in authoritative order.

Do not add selected row, selected tab, scroll position, renderer component names, WinUI button identity, or Steam control identity. Those are presentation-local.

### 4.2 Move intent

Add:

```csharp
public sealed record AddonQuickSettingsTabOrderMoveIntent(
    AddonQuickSettingsTabId TabId,
    int Delta);
```

Valid product move:

```text
Delta = -1  → move one position earlier
Delta = +1  → move one position later
```

No arbitrary destination index in PR3. No drag/drop contract. No generic reorder command language.

### 4.3 Mutation result

Add:

```csharp
public sealed record AddonQuickSettingsTabOrderMutationResult(
    bool Succeeded,
    string? FailureMessage,
    AddonQuickSettingsTabOrderSnapshot State);
```

The returned `State` is always the authoritative Runtime readback when Runtime is available.

A failed mutation must not return an optimistic proposal as truth.

---

## 5. One shared projection and move algorithm

Add one small closed product helper.

Preferred shape:

```csharp
public static class AddonQuickSettingsTabOrderProduct
{
    public static AddonQuickSettingsTabOrderSnapshot Create(
        IReadOnlyList<AddonQuickSettingsTabId>? order)
    {
        var normalized =
            AddonQuickSettingsTabOrderContract.NormalizeOrDefault(order);

        var rows = normalized
            .Select((tabId, index) =>
                new AddonQuickSettingsTabOrderRow(
                    tabId,
                    AddonQuickSettingsShellContract.LabelFor(tabId),
                    CanMoveEarlier: index > 0,
                    CanMoveLater: index < normalized.Count - 1))
            .ToArray();

        return new(true, rows);
    }

    public static bool TryCreateMovedOrder(
        IReadOnlyList<AddonQuickSettingsTabId>? current,
        AddonQuickSettingsTabOrderMoveIntent? intent,
        out IReadOnlyList<AddonQuickSettingsTabId> proposed)
    {
        proposed = Array.Empty<AddonQuickSettingsTabId>();

        if (!AddonQuickSettingsTabOrderContract.TryNormalize(
                current, out var normalized) ||
            intent is null ||
            !Enum.IsDefined(intent.TabId) ||
            intent.Delta is not (-1 or 1))
        {
            return false;
        }

        var index = -1;
        for (var i = 0; i < normalized.Count; i++)
        {
            if (normalized[i] == intent.TabId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return false;

        var target = index + intent.Delta;
        if (target < 0 || target >= normalized.Count)
            return false;

        var moved = normalized.ToArray();
        (moved[index], moved[target]) = (moved[target], moved[index]);

        return AddonQuickSettingsTabOrderContract.TryNormalize(
            moved, out proposed);
    }
}
```

Do not add LINQ/list helper abstractions merely for this.

Required behavior:

```text
unknown TabId        → false
Delta 0              → false
Delta ±2             → false
first + earlier      → false
last + later         → false
valid one-step move  → complete valid five-item proposal
```

This becomes the product move meaning.

Remove the duplicate move-construction algorithm from `OverlayTabState`.

---

## 6. Runtime/frontend control becomes the common mutation seam

Extend `IAddonFrontendControl` with narrow defaults.

Conceptually:

```csharp
Task<AddonQuickSettingsTabOrderSnapshot>
    CaptureAddonQuickSettingsTabOrderAsync(
        CancellationToken cancellationToken = default)
    => Task.FromResult(
        AddonQuickSettingsTabOrderSnapshot.Unavailable());

Task<AddonQuickSettingsTabOrderMutationResult>
    MoveAddonQuickSettingsTabAsync(
        AddonQuickSettingsTabOrderMoveIntent intent,
        CancellationToken cancellationToken = default)
    => Task.FromResult(
        new AddonQuickSettingsTabOrderMutationResult(
            false,
            "Tab order is unavailable.",
            AddonQuickSettingsTabOrderSnapshot.Unavailable()));
```

Do not route Setting through `QuickSettingsPageId`.

Do not add `Setting` to `QuickSettingsPageId`.

This is intentionally a dedicated special-page contract.

---

## 7. In-process Runtime implementation

Implement both methods in `InProcessAddonFrontendControl`.

### Capture

```csharp
public Task<AddonQuickSettingsTabOrderSnapshot>
    CaptureAddonQuickSettingsTabOrderAsync(
        CancellationToken cancellationToken = default)
{
    ThrowIfShuttingDown();
    cancellationToken.ThrowIfCancellationRequested();

    return Task.FromResult(
        AddonQuickSettingsTabOrderProduct.Create(
            _settings.AddonQuickSettingsTabOrder));
}
```

### Mutation

The Runtime computes the proposal from its **current authoritative order**.

The renderer never sends a full replacement order.

Conceptually:

```csharp
public Task<AddonQuickSettingsTabOrderMutationResult>
    MoveAddonQuickSettingsTabAsync(
        AddonQuickSettingsTabOrderMoveIntent intent,
        CancellationToken cancellationToken = default)
{
    ThrowIfShuttingDown();
    cancellationToken.ThrowIfCancellationRequested();

    var before = _settings.AddonQuickSettingsTabOrder;

    if (!AddonQuickSettingsTabOrderProduct.TryCreateMovedOrder(
            before, intent, out var proposed))
    {
        return Task.FromResult(
            new AddonQuickSettingsTabOrderMutationResult(
                false,
                "The requested tab-order move is not valid.",
                AddonQuickSettingsTabOrderProduct.Create(before)));
    }

    try
    {
        if (!_settings.TryChangeAddonQuickSettingsTabOrder(proposed))
        {
            return Task.FromResult(
                new AddonQuickSettingsTabOrderMutationResult(
                    false,
                    "The requested tab order was rejected.",
                    AddonQuickSettingsTabOrderProduct.Create(
                        _settings.AddonQuickSettingsTabOrder)));
        }
    }
    catch (Exception exception)
    {
        AppLog.Warn(
            "QuickSettings",
            "Tab-order persistence failed; keeping the authoritative order.",
            exception);

        return Task.FromResult(
            new AddonQuickSettingsTabOrderMutationResult(
                false,
                "Failed to save the tab order.",
                AddonQuickSettingsTabOrderProduct.Create(
                    _settings.AddonQuickSettingsTabOrder)));
    }

    var after = _settings.AddonQuickSettingsTabOrder;

    if (!before.SequenceEqual(after))
        StateInvalidated?.Invoke(this, EventArgs.Empty);

    return Task.FromResult(
        new AddonQuickSettingsTabOrderMutationResult(
            true,
            null,
            AddonQuickSettingsTabOrderProduct.Create(after)));
}
```

The exact log category may follow local convention.

Do not move settings persistence into a new service.

`StartupSettingsCoordinator.TryChangeAddonQuickSettingsTabOrder` remains the persistence authority.

The new frontend method is an adapter over it.

---

## 8. Keep central complete-order validation

Do not delete or weaken:

```csharp
StartupSettingsCoordinator.TryChangeAddonQuickSettingsTabOrder(...)
```

It remains the final complete-order validation/persistence backstop.

Tests must continue proving:

- malformed complete order rejected;
- duplicate order rejected;
- missing identity rejected;
- unknown identity rejected;
- current valid order unchanged on rejection.

The new move product does not make central persistence validation redundant.

---

## 9. Frontend transport: v31 → v32

PR3 adds two real RPCs to `.Frontend/.Qam`.

Therefore:

```text
FrontendTransportProtocol
31 → 32
```

Add a version comment.

Add:

```text
CaptureAddonQuickSettingsTabOrder
MoveAddonQuickSettingsTab
```

to `FrontendRpcMethod`.

### Capture

No request payload.

Unexpected payload must be rejected without invoking Runtime.

### Move

Use the shared intent directly as payload:

```text
AddonQuickSettingsTabOrderMoveIntent
```

Response:

```text
AddonQuickSettingsTabOrderMutationResult
```

Do not add a transport-only copy of the intent/result.

---

## 10. NamedPipeAddonFrontendClient

Add:

```csharp
public Task<AddonQuickSettingsTabOrderSnapshot>
    CaptureAddonQuickSettingsTabOrderAsync(
        CancellationToken cancellationToken = default)
    => SendAsync<AddonQuickSettingsTabOrderSnapshot>(
        FrontendRpcMethod.CaptureAddonQuickSettingsTabOrder,
        payload: null,
        cancellationToken);

public Task<AddonQuickSettingsTabOrderMutationResult>
    MoveAddonQuickSettingsTabAsync(
        AddonQuickSettingsTabOrderMoveIntent intent,
        CancellationToken cancellationToken = default)
    => SendAsync<AddonQuickSettingsTabOrderMutationResult>(
        FrontendRpcMethod.MoveAddonQuickSettingsTab,
        intent,
        cancellationToken);
```

Follow the actual current `SendAsync` signature.

Do not add retry machinery.

---

## 11. NamedPipeAddonFrontendServer

Add two switch cases.

Capture must enforce no payload.

Move must require a payload and deserialize the shared intent.

Transport validation remains structural.

Runtime product validation owns:

- valid TabId;
- valid delta;
- current boundary;
- proposal construction;
- persistence.

Do not duplicate the whole move algorithm inside transport.

---

## 12. QAM bridge

Add exactly:

```text
captureQuickSettingsTabOrder
moveQuickSettingsTab
```

Preferred mapping:

```csharp
"captureQuickSettingsTabOrder"
    => await _client.CaptureAddonQuickSettingsTabOrderAsync(token),

"moveQuickSettingsTab"
    => await MoveQuickSettingsTabAsync(root, token),
```

and:

```csharp
private async Task<object> MoveQuickSettingsTabAsync(
    JsonElement root,
    CancellationToken token)
{
    var intent =
        root.GetProperty("payload")
            .Deserialize<AddonQuickSettingsTabOrderMoveIntent>(
                QuickSettingsBridgeJson)
        ?? throw new JsonException(
            "Invalid tab-order move intent.");

    return await _client.MoveAddonQuickSettingsTabAsync(
        intent, token).ConfigureAwait(false);
}
```

Do not add a generic action-string dispatcher.

Do not reuse `mutateQuickSetting` for Setting.

---

## 13. QAM Setting renderer — replace only the Setting placeholder

PR #528 currently handles:

```javascript
case AQS_TAB_CONTROLLER:
case AQS_TAB_SHORTCUT:
case AQS_TAB_SETTING:
  // placeholder
```

After PR3:

```text
Controller → placeholder
Shortcut   → placeholder
Setting    → functional typed Setting renderer
```

Do not touch Controller/Shortcut product semantics.

---

## 14. Do not add another Steam-native resolver in PR3

Use the existing `native.SliderField`.

Each Setting row represents one tab's current position.

Conceptual product-to-QAM mapping:

```text
AddonQuickSettingsTabOrderRow
  TabId
  Label
  CanMoveEarlier
  CanMoveLater

→ Steam native SliderField

slider value = current row index
min = 0
max = 4
step = 1

left one notch
→ Delta -1

right one notch
→ Delta +1
```

This is surface-specific presentation of the same one-step move meaning.

Why this is preferred: it avoids adding ButtonItem/DialogButton discovery, another Steam module fingerprint, fake HTML arrow buttons, a 10-button Setting page, and custom focus routing.

---

## 15. QAM Setting state

Add a narrow validator for bridge data.

Conceptually:

```javascript
function validateTabOrderState(state) {
  if (state?.available !== true ||
      !Array.isArray(state.rows) ||
      state.rows.length !== 5) {
    return null;
  }

  const seen = new Set();

  for (const row of state.rows) {
    if (!KNOWN_AQS_TAB_IDS.has(row?.tabId) ||
        seen.has(row.tabId) ||
        typeof row.label !== "string" ||
        row.label.length === 0 ||
        typeof row.canMoveEarlier !== "boolean" ||
        typeof row.canMoveLater !== "boolean") {
      return null;
    }

    seen.add(row.tabId);
  }

  return state;
}
```

Do not reconstruct canonical order locally.

Do not hard-code labels.

Do not silently repair invalid payload.

---

## 16. QAM Setting component

A small component inside the existing `buildAddonTab(...)` scope is sufficient.

Conceptual shape:

```javascript
function SettingTabOrderPanel({
  tabOrderState,
  busy,
  error,
  onMove,
}) {
  if (!tabOrderState?.available) {
    return React.createElement(
      native.PanelSection,
      { title: "Tab Order" },
      React.createElement(
        native.PanelSectionRow,
        null,
        React.createElement(
          "p",
          null,
          error || "Tab order is unavailable.")));
  }

  const rows = tabOrderState.rows.map((row, index) => {
    const notchLabels = tabOrderState.rows.map(
      (_, notchIndex) => ({
        notchIndex,
        label: String(notchIndex + 1),
        value: notchIndex,
      }));

    return React.createElement(
      native.PanelSectionRow,
      { key: `tab-order-${row.tabId}` },
      React.createElement(native.SliderField, {
        label: row.label,
        min: 0,
        max: tabOrderState.rows.length - 1,
        step: 1,
        value: index,
        notchCount: tabOrderState.rows.length,
        notchLabels,
        notchTicksVisible: true,
        showValue: true,
        disabled: busy,
        onChange: next => {
          const target = Math.round(Number(next));
          const delta = Math.sign(target - index);

          if (delta === -1 && row.canMoveEarlier)
            void onMove(row.tabId, -1);
          else if (delta === 1 && row.canMoveLater)
            void onMove(row.tabId, +1);
        },
      }));
  });

  return React.createElement(
    native.PanelSection,
    { title: "Tab Order" },
    ...rows);
}
```

Exact `SliderField` props should follow the already-working QAM renderer conventions.

Do not add trailing debounce.

Tab-order moves are immediate discrete mutations.

---

## 17. QAM mutation flow

One move at a time.

Do not optimistically reorder.

Conceptually:

```javascript
const moveTab = async (tabId, delta) => {
  if (tabOrderBusy) return;

  setTabOrderBusy(true);
  setTabOrderError(null);

  try {
    const result = await request(
      "moveQuickSettingsTab",
      { tabId, delta });

    const authoritative =
      validateTabOrderState(result?.state);

    if (!authoritative) {
      setTabOrderError(
        "Tab order is unavailable.");
      return;
    }

    setTabOrderState(authoritative);

    // Keep the outer shared shell visually converged
    // with the authoritative Setting state immediately.
    setShellTabs(
      authoritative.rows.map(row => ({
        tabId: row.tabId,
        label: row.label,
      })));

    if (result?.succeeded !== true)
      setTabOrderError(
        result?.failureMessage ||
        "Tab order update failed.");
  }
  catch (_) {
    setTabOrderError(
      "Tab order update failed.");
  }
  finally {
    setTabOrderBusy(false);
  }
};
```

The user's currently selected inner tab identity must remain selected.

Reordering the tabs must not reset `activeTab`.

All five identities always remain present.

---

## 18. QAM initial state + event-driven refresh

The current Addon panel captures the shell once.

PR3 needs Setting state as well.

Initial mount:

```text
captureQuickSettingsShell
captureQuickSettingsTabOrder
captureStatus for initial Device/Profile preference
```

Do not add polling.

### Subsequent StateInvalidated

Reuse the existing QAM bridge notification/subscriber path.

The Addon shell owner may subscribe and refresh:

```text
shell snapshot
tab-order snapshot
```

without recalculating the initial selected tab.

Important:

```text
initial mount
→ may choose Device/Profile from active-game context

later invalidation
→ refresh order/state
→ NEVER steal activeTab from the user
```

Do not rerun initial-selection policy after every tab-order or Device/Profile invalidation.

No epoch/barrier is required.

If a shell/state pair is captured across one ordinary concurrent mutation boundary, the following mutation result or invalidation converges it. Do not introduce transaction generations for this UI preference.

---

## 19. Overlay protocol: v7 → v8

PR3 changes the tab-order wire semantics from:

```text
raw whole-order state
+
raw whole-order SetTabOrder request
```
to:

```text
shared typed state
+
shared one-position move intent/result
```

Therefore:

```text
OverlayTransportProtocol
7 → 8
```

Pre-release product: no compatibility shim.

Do not preserve parallel v7 tab-order fields.

### Preferred wire shape

Extend/replace the tab-order fields of `OverlayWireMessage` with:

```csharp
AddonQuickSettingsTabOrderSnapshot? TabOrderState = null,
AddonQuickSettingsTabOrderMoveIntent? TabOrderMove = null,
AddonQuickSettingsTabOrderMutationResult? TabOrderMutationResult = null
```

Use clear message kinds, for example:

```text
TabOrderState
TabOrderMoveRequest
TabOrderMoveResult
```

Do not keep `SetTabOrder` if it no longer sends a complete order.

---

## 20. Overlay server must use the same frontend-control mutation

Change the tab-order delegates from:

```text
Func<IReadOnlyList<TabId>> getTabOrder
Func<IReadOnlyList<TabId>, bool> tryChangeTabOrder
```

to the shared typed operations:

```csharp
Func<CancellationToken,
     Task<AddonQuickSettingsTabOrderSnapshot>>
    captureTabOrder;

Func<AddonQuickSettingsTabOrderMoveIntent,
     CancellationToken,
     Task<AddonQuickSettingsTabOrderMutationResult>>
    moveTabOrder;
```

The default/no-authority test fallback should return Unavailable / failed result.

Do not let `OverlayWire` call `StartupSettingsCoordinator` directly.

Both QAM and Overlay must reach the same:

```text
IAddonFrontendControl.MoveAddonQuickSettingsTabAsync
```

mutation seam.

---

## 21. Overlay initial state before Ready remains mandatory

Preserve the existing lifecycle invariant:

```text
HandshakeAccepted
→ authoritative tab-order state
→ Overlay applies it
→ Overlay reports Ready
```

Only the payload type changes.

Do not let a Ready Overlay briefly expose stale default order.

Initial tab-order capture failure should be feature-local and bounded.

Do not affect OQ4 capture ownership.

---

## 22. Overlay mutation reply

Current behavior already treats authoritative readback as the result.

Keep that principle.

Preferred flow:

```text
Overlay UI
→ TabOrderMoveIntent
→ server
→ IAddonFrontendControl.MoveAddonQuickSettingsTabAsync
→ AddonQuickSettingsTabOrderMutationResult
→ client
→ Window applies result.State
→ optional local error/log from FailureMessage
```

No optimistic reordering.

A failed mutation still applies the returned authoritative state.

---

## 23. OverlayWindow no longer constructs the product proposal

Remove:

```text
OverlayTabState.TryCreateMovedOrder(...)
```

from the product path.

`OverlayTabState` remains the Overlay-local owner of:

- selected tab;
- current applied order for shell navigation;
- reset-to-first-on-Show behavior;
- LB/RB local selection.

That is presentation state and should remain Overlay-local.

### New Window event

Prefer:

```csharp
internal event Action<AddonQuickSettingsTabOrderMoveIntent>?
    TabOrderMoveRequested;
```

and:

```csharp
private void RequestTabOrderMove(
    AddonQuickSettingsTabId tab,
    int delta)
{
    TabOrderMoveRequested?.Invoke(
        new(tab, delta));
}
```

Boundary suppression comes from the authoritative row state.

Do not reconstruct a whole order in the Window.

---

## 24. Overlay row binds authoritative row state

Update the WinUI row so it can apply the shared state.

Conceptually:

```csharp
internal void ApplyState(
    AddonQuickSettingsTabOrderRow state)
{
    _label.Text = state.Label;
    _moveEarlier.IsEnabled = state.CanMoveEarlier;
    _moveLater.IsEnabled = state.CanMoveLater;
}
```

Keep row instances stable.

Do not recreate all five WinUI controls after every move.

The Window should continue to reposition existing row containers with `Grid.SetRow`.

---

## 25. Overlay ApplyTabOrder becomes typed-state application

Preferred signature:

```csharp
internal void ApplyTabOrderState(
    AddonQuickSettingsTabOrderSnapshot state)
```

If unavailable/malformed:

- log feature-local failure;
- keep current valid applied order;
- do not close Overlay;
- do not change controller capture.

For valid state:

- derive authoritative order from `state.Rows`;
- apply to `OverlayTabState.TryApplyOrder`;
- reposition tab buttons;
- reposition Setting rows;
- apply row labels/boundary flags;
- preserve selected top-level tab;
- preserve selected Setting row identity;
- preserve scroll;
- next Show still resets to current first configured tab.

This preserves existing OQ5-UI-10 behavior.

---

## 26. Overlay App changes

Rename the narrow callback path conceptually:

```text
TabOrderChangeRequested
→ TabOrderMoveRequested

SendTabOrderAsync(order)
→ SendTabOrderMoveAsync(intent)
```

`NamedPipeOverlayClient` stays owned by App.

Do not pass the client into `OverlayWindow`.

On a mutation result:

- marshal `result.State` to UI;
- apply authoritative state;
- log/display bounded local failure if `Succeeded == false`;
- never apply the original intent optimistically.

---

## 27. OverlayProcessController binding

Replace direct coordinator delegates with the common frontend-control seam.

Conceptually:

```csharp
_overlayController.BindTabOrderAuthority(
    capture: token =>
        _frontendControl!
            .CaptureAddonQuickSettingsTabOrderAsync(token),
    move: (intent, token) =>
        _frontendControl!
            .MoveAddonQuickSettingsTabAsync(intent, token));
```

The exact binding method name may remain `BindTabOrderAuthority`.

Do not add a manager class around two delegates.

---

## 28. Cross-surface event-driven convergence

A successful Runtime move should raise:

```text
IAddonFrontendControl.StateInvalidated
```

once.

That already feeds QAM bridge notifications.

### QAM

The PR3 Addon/Setting subscriber refreshes shared shell + tab-order state.

### Overlay

Extend the existing visible/captured invalidation path with one tab-order refresh.

Preferred:

```csharp
_ = _overlayController.RefreshQuickSettingsAsync();
_ = _overlayController.RefreshTabOrderAsync();
```

or an equivalently small existing refresh composition.

Do not create a new event bus. Do not create a shell epoch. Do not poll.

Why this is enough:

```text
QAM moves tab
→ Runtime persists
→ StateInvalidated
→ QAM refreshes
→ visible Overlay refreshes

Overlay moves tab
→ Runtime returns authoritative result directly
→ StateInvalidated
→ QAM refreshes
→ Overlay already applies mutation result
```

Duplicate same-state publication is acceptable.

Do not add deduplication machinery solely for that.

---

## 29. Overlay live tab-order refresh

Add one narrow Runtime → Overlay publication method.

For example:

```csharp
internal async Task RefreshTabOrderAsync()
```

Behavior:

- only when server exists;
- only when Ready + Visible for live refresh;
- capture through the bound typed delegate;
- send one typed `TabOrderState`;
- capture/publish failure is feature-local;
- never changes visibility/capture ownership.

The mandatory pre-Ready initial tab-order state remains a separate handshake path.

Do not reuse the Device/Profile mutation-in-flight gate for this preference unless a concrete conflict exists.

---

## 30. Do not make Setting depend on OQ4 mutation admission

The Setting tab order is a persisted frontend preference.

It is not a hardware/controller mutation.

Preserve existing practical behavior:

- Overlay UI can request it through its admitted connected session;
- QAM can request it through QAM frontend transport;
- Runtime validates/persists it;
- controller ownership is unaffected.

Do not introduce presentation pause requirements, controller-neutral checks, Steam/BPM routing requirements, PID checks, or HidHide checks.

---

## 31. Protocol/version summary

Expected PR3 protocol changes:

```text
FrontendTransportProtocol
31 → 32

OverlayTransportProtocol
7 → 8
```

Why:

```text
.Frontend/.Qam
+ CaptureAddonQuickSettingsTabOrder
+ MoveAddonQuickSettingsTab

.Overlay
raw whole-order SetTabOrder
→ typed shared state + one-position move intent/result
```

Do not bump any controller/native protocol.

---

## 32. Persistence compatibility

The persisted settings JSON remains:

```text
"OverlayTabOrder"
```

even though the in-memory/product naming is Addon Quick Settings.

Required regression:

```text
existing settings file
→ loads unchanged

PR3 move
→ writes same historical key

new key "AddonQuickSettingsTabOrder"
→ must NOT appear
```

Do not treat this as a reason to keep Overlay-only product semantics.

Disk compatibility and product naming are separate concerns.

---

## 33. QAM shell consistency after a move

The outer inner-tab bar and Setting rows must converge immediately from authoritative result state.

Do not wait for a later arbitrary Device/Profile invalidation.

After a valid mutation result:

```text
result.State.Rows
→ Setting rows
→ inner tab order/labels
```

Selected `activeTab` identity remains unchanged.

Example:

```text
before
Device | Profile | Controller | Shortcut | Setting
selected = Setting

move Setting earlier
→ authoritative result:
Device | Profile | Controller | Setting | Shortcut

selected still = Setting
```

No global selected-tab state is added.

---

## 34. Boundary behavior

Both renderers receive:

```text
CanMoveEarlier
CanMoveLater
```

from the shared state.

### Overlay

Disable the corresponding arrow button.

Controller Left/Right on that Setting row must emit no request at a disabled boundary.

### QAM

The native position slider naturally reaches min/max.

The handler must also check the corresponding boolean before sending.

### Runtime

The shared product helper remains the final boundary validation.

Thus:

```text
renderer suppresses obvious boundary request
+
Runtime rejects malformed/stale boundary intent
```

No extra synchronization is needed.

---

## 35. Failure behavior

### Persistence failure

Return:

```text
Succeeded = false
FailureMessage = bounded product message
State = freshly captured current Runtime truth
```

QAM/Overlay stay alive.

### Malformed intent

Reject centrally.

No settings write.

Return current authoritative state when request reached Runtime as a typed request.

Malformed transport JSON remains a transport error.

### QAM renderer failure

Only QAM Setting becomes unavailable/error.

Runtime and Overlay survive.

### Overlay renderer failure

Existing Overlay failure/lifecycle handling remains.

Runtime and QAM survive.

### Cross-surface refresh failure

The mutating surface already received authoritative result.

The other surface catches up on next successful event/reopen.

Do not add retry loops solely for UI preference convergence.

---

## 36. Full1902 invariants — untouched

PR3 must not change:

- Center M authority;
- PID1901/PID1902 desired state;
- reboot-bound authority transition;
- DirectInput owner;
- HidHide Applications/hidden devices;
- VIIPER ownership;
- X360/SteamDeck presentation ownership;
- Steam/BPM presentation selection;
- PnP recovery;
- sleep/hibernate/resume;
- restart/shutdown teardown;
- fail-close controller policy;
- OQ4 pause/neutral/release;
- WING/OEM1 mapping.

No Setting state becomes controller authority.

---

## 37. Overengineering guardrails

Do not create:

- SettingPageManager;
- TabOrderManager;
- provider/plugin registry;
- generic reorder framework;
- arbitrary destination/index command protocol;
- drag/drop abstraction;
- global selected-tab service;
- cross-surface UI transaction;
- epoch/generation synchronization;
- polling;
- retry scheduler;
- schema-to-React/XAML layer;
- new process;
- new pipe.

Use:

```text
one shared state contract
one shared move intent
one shared Runtime mutation
two thin renderers
existing transports
existing StateInvalidated event
```

---

## 38. Expected production files

Likely changes:

```text
src/SteamInputAddonforClaw.Contracts/Frontend/
  AddonQuickSettingsTabOrderContracts.cs
  FrontendContracts.cs

src/SteamInputAddonforClaw/Frontend/
  InProcessAddonFrontendControl.cs

src/SteamInputAddonforClaw.FrontendTransport/
  FrontendWire.cs
  NamedPipeAddonFrontendClient.cs
  NamedPipeAddonFrontendServer.cs
  OverlayWire.cs

src/SteamInputAddonforClaw.QamHost/
  QamFrontendBridge.cs
  Frontend/qam.js

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

Possible comment-only cleanup:

```text
AppSettings.cs
```

`SettingsStore.cs` should normally need no behavior change except tests/comments if required.

Do not touch controller ownership code.

---

## 39. Required contract tests

Add focused tests for `AddonQuickSettingsTabOrderProduct`.

Required:

1. default state contains exactly five rows;
2. row order follows the supplied valid order;
3. labels come from `AddonQuickSettingsShellContract.LabelFor`;
4. first row cannot move earlier;
5. last row cannot move later;
6. middle rows allow both;
7. valid earlier move swaps exactly one adjacent pair;
8. valid later move swaps exactly one adjacent pair;
9. boundary move rejected;
10. delta 0 rejected;
11. delta other than ±1 rejected;
12. unknown TabId rejected;
13. malformed current order rejected for mutation;
14. returned proposal is a complete valid five-item order.

Do not write a generic reorder test framework.

---

## 40. Settings/coordinator regression tests

Keep all current `TryChangeAddonQuickSettingsTabOrder` tests.

Add/retain:

- valid complete order persists;
- invalid complete order does not persist;
- same order accepted no-op;
- disk key remains `OverlayTabOrder`;
- new `AddonQuickSettingsTabOrder` JSON key is absent.

---

## 41. In-process seam tests

Add tests to `QuickSettingsInProcessSeamTests` or a focused sibling file.

Required:

### Capture

```text
settings custom order
→ CaptureAddonQuickSettingsTabOrderAsync
→ same five identities/order/labels/boundaries
```

### Move

```text
valid move
→ coordinator persisted new order
→ result Succeeded
→ result.State is readback
→ one StateInvalidated
```

### Boundary

```text
boundary move
→ no settings write
→ Succeeded false
→ authoritative state unchanged
→ no StateInvalidated
```

### Persistence failure

Use the existing settings-store failure pattern if present.

```text
save throws
→ result Succeeded false
→ current state retained
→ no StateInvalidated
```

Do not add race-only tests for impossible multi-session writers.

---

## 42. Frontend transport tests

Update expected protocol:

```text
31 → 32
```

Add:

- typed tab-order capture round-trip;
- typed move round-trip;
- returned mutation result round-trip;
- capture rejects unexpected payload without Runtime invocation;
- malformed move payload rejected safely;
- unknown RPC behavior unchanged;
- protocol mismatch still fails handshake.

Do not remove existing PR1 shell transport coverage.

---

## 43. QAM bridge tests

Add:

```text
captureQuickSettingsTabOrder
moveQuickSettingsTab
```

Required:

- capture reaches the client exactly once;
- move intent TabId/Delta round-trips;
- malformed move payload returns bounded bridge error;
- unsupported methods still rejected;
- existing Device/Profile generic bridge restrictions unchanged.

Do not route Setting through `mutateQuickSetting`.

---

## 44. QAM source-contract tests

Update `QamFrontendContractTests`.

Required assertions:

### Setting placeholder removed only for Setting

```text
Setting
→ SettingTabOrderPanel

Controller
→ placeholder

Shortcut
→ placeholder
```

### No local product order

Prevent:

```javascript
["Device", "Profile", "Controller", "Shortcut", "Setting"]
```

and any QAM-only canonical label/order table.

### Uses typed bridge

Assert:

```text
captureQuickSettingsTabOrder
moveQuickSettingsTab
```

### Uses existing native SliderField

Assert Setting uses:

```text
native.SliderField
```

and no new custom HTML range input / custom arrow-button DOM implementation is introduced.

### No new Steam native resolver

PR3 should not add ButtonItem/DialogButton discovery.

### No optimistic reorder

The mutation flow updates state from:

```text
result.state
```

not from a locally swapped array.

### Active tab preserved

Refresh/mutation must not call the initial Device/Profile selection logic again.

### Event-driven

No `setInterval`, polling loop, MutationObserver, timeout retry.

---

## 45. Overlay transport tests — v8

Update `AddonQuickSettingsTabOrderTransportTests`.

Required:

### Initial handshake

```text
HandshakeAccepted
→ typed AddonQuickSettingsTabOrderSnapshot
→ client applies it
→ Ready
```

### Move

```text
SendTabOrderMoveAsync(intent)
→ server calls same typed move delegate
→ typed mutation result
→ client applies authoritative result.State
```

### Failure

```text
Succeeded false
→ state still applied
→ connection stays alive
```

### Malformed request

Bad shape must fail closed according to current Overlay protocol policy.

### Concurrency regression

Keep the one instance write gate.

Quick Settings page publication / navigation / tab-order frames may not interleave bytes.

---

## 46. Overlay editor tests

Move product-semantic tests out of `OverlayTabState`.

After PR3, `OverlayTabState` tests should cover only presentation state:

- apply valid authoritative order;
- selected top-level tab preserved;
- ResetForShow uses new first;
- LB/RB follows applied order.

Setting editor tests should prove:

- row label comes from typed state;
- earlier/later enabled flags come from typed state;
- clicking enabled move emits `AddonQuickSettingsTabOrderMoveIntent`;
- disabled boundary emits no intent;
- authoritative state reorder preserves selected Setting row identity;
- visible top-level Setting tab remains selected;
- row instances are reused.

Do not keep `TryCreateMovedOrder` as an Overlay product test after it is removed.

---

## 47. Cross-surface parity tests

Add one focused contract-level test proving the same snapshot is consumable by both surface adapters.

This does not need to instantiate full Steam/WinUI.

At minimum verify:

```text
custom Runtime order
→ AddonQuickSettingsTabOrderSnapshot.Rows

Overlay order projection
→ same TabIds/labels/boundaries

QAM expected row mapping
→ same TabIds/labels/boundaries
```

Do not build a generic renderer-test harness.

---

## 48. AddonProcessHost / invalidation tests

Add or update source/behavior tests to prove:

- Overlay tab-order binding uses `IAddonFrontendControl` typed capture/move seam;
- direct `StartupSettingsCoordinator.TryChangeAddonQuickSettingsTabOrder` binding from `AddonProcessHost` is removed;
- frontend `StateInvalidated` still drives visible Overlay refresh;
- tab-order live refresh is event-driven;
- no polling.

Do not alter OQ4 admission logic.

---

## 49. Manual QAM acceptance

On a real MSI Claw / supported Steam GamepadUI:

### Setting page

Verify:

- Setting is no longer placeholder;
- exactly five tab-order rows;
- labels/order match current configured shell;
- native Steam slider/focus behavior;
- left/right moves one position;
- first row cannot move earlier;
- last row cannot move later;
- no duplicate request while one mutation is busy.

### Reorder effect

Move one tab.

Verify immediately:

- Setting rows reorder from authoritative result;
- inner QAM tab strip reorders;
- currently selected inner tab identity stays selected.

### Reopen

Close/reopen QAM.

Verify persisted order remains.

### Active-game selection regression

Fresh open still prefers Device/Profile according to PR2 policy.

A later tab-order invalidation must not steal selection.

---

## 50. Manual Overlay acceptance

Verify:

- Setting page still looks/behaves like current WinUI editor;- same five rows;
- arrow controls still move one position;
- controller Left/Right still works;
- boundary move sends nothing;
- order changes only after authoritative result;
- selected Setting row identity survives reorder;
- selected top-level Setting tab survives reorder;
- body scroll is not reset;
- next Show starts on the new first configured tab.

OQ4 capture/release behavior must be unchanged.

---

## 51. Cross-surface manual acceptance

Test both directions.

### QAM → Overlay

```text
open QAM
move tab
close QAM
open Overlay
→ same persisted order
```

If Overlay is already visible in a supported test flow, it should receive event-driven authoritative refresh without polling.

### Overlay → QAM

```text
open Overlay
move tab
close Overlay
open QAM
→ same persisted order
```

If QAM is already mounted, the existing invalidation path should converge it.

No requirement to force both windows visible simultaneously.

---

## 52. Build and validation

Run:

```powershell
dotnet restore SteamInputAddonforClaw.slnx

dotnet build SteamInputAddonforClaw.slnx `
  --no-restore `
  -v:minimal

dotnet test SteamInputAddonforClaw.slnx `
  --no-restore `
  --logger "console;verbosity=minimal"
```

Focused during iteration:

```powershell
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj `
  --no-restore `
  --filter "FullyQualifiedName~AddonQuickSettingsTabOrder|FullyQualifiedName~QamFrontend|FullyQualifiedName~OverlayTabOrder|FullyQualifiedName~FrontendNamedPipe"
```

Also:

```powershell
node --check src/SteamInputAddonforClaw.QamHost/Frontend/qam.js
git diff --check
```

Focused tests do not replace the full suite.

---

## 53. Definition of done

- [ ] Baseline is PR #528 / `fe4677f...` or a later rebased `main`.
- [ ] One dedicated shared Setting tab-order state contract exists.
- [ ] One shared `TabId + Delta` move intent exists.
- [ ] One shared typed mutation result exists.
- [ ] Runtime computes the moved order from current authoritative state.
- [ ] Overlay no longer owns the move-proposal algorithm.
- [ ] `StartupSettingsCoordinator` remains the persistence authority.
- [ ] Existing complete-order central validation remains.
- [ ] Disk key remains `OverlayTabOrder`.
- [ ] Frontend protocol is v32.
- [ ] Overlay protocol is v8.
- [ ] QAM bridge exposes typed capture/move methods.
- [ ] QAM Setting placeholder is replaced by functional native Setting UI.
- [ ] QAM uses existing native `SliderField`; no new native component resolver is added.
- [ ] Controller remains placeholder.
- [ ] Shortcut remains placeholder until PR4.
- [ ] QAM does not optimistically reorder.
- [ ] Overlay does not optimistically reorder.
- [ ] Both surfaces apply authoritative result state.
- [ ] Boundary moves emit no normal renderer request.
- [ ] Runtime still rejects invalid/stale boundary intent.
- [ ] Successful move raises existing `StateInvalidated`.
- [ ] QAM convergence is event-driven.
- [ ] Visible Overlay convergence is event-driven.
- [ ] No polling/epoch/barrier is introduced.
- [ ] User-selected QAM inner tab is not stolen by refresh.
- [ ] Overlay selected top-level tab/Setting row identity is preserved.
- [ ] Full1902/HidHide/VIIPER/OQ4 behavior is untouched.
- [ ] Focused tests pass.
- [ ] Full suite passes.
- [ ] Real QAM acceptance passes.
- [ ] Real Overlay acceptance passes.
- [ ] Cross-surface persisted order parity passes.

---

## 54. Review standard

Block only realistic product defects.

Blocking examples:

- QAM and Overlay reach different Runtime mutation paths;
- Overlay still computes/persists a separate product order;
- QAM invents a local five-tab label/order table;
- a boundary action can persist an invalid order;
- mutation result is ignored and optimistic local order is shown as truth;
- QAM reorder resets the user's selected inner tab;
- Overlay reorder resets the selected top-level tab or breaks OQ4 capture;
- QAM-originated persisted order is not visible after reopening Overlay;
- Overlay-originated persisted order is not visible after reopening QAM;
- protocol payload change is made without v32/v8 bump;
- existing `OverlayTabOrder` settings compatibility is broken;
- Setting mutation touches controller ownership/lifecycle.

Do not block for:

- theoretical instruction-level races with no normal lifecycle path;
- simultaneous unsupported multi-session writers;
- future drag/drop extensibility;
- a hypothetical generic reorderable-page framework;
- pixel-level parity between Steam native and WinUI renderers.

The intended result is deliberately small:

> one Runtime-owned tab-order product, one one-step move meaning, QAM native presentation, WinUI presentation, and authoritative readback on both surfaces.