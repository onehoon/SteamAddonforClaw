# Work Order — QAM Fresh-Open Addon Tab Selection via Causal Runtime Intent

> **Date:** 2026-09-19  
> **Status:** Ready for implementation  
> **Reviewed production baseline:** `main` at `93055fbf25b613916f61f330b8c4bf9c16d900ad`  
> **Architecture authority:** standalone Full PID1902  
> **Primary QAM design authority:** `docs/QAM_SINGLE_ADDON_TAB_NATIVE_INNER_TABS_RESEARCH_AND_DESIGN_2026-09-13.md`  
> **Reference implementation reviewed:** `KillerPixelCrew/WSGM` branch `2.0` at `3c57b862dcc5e9964910aac2e5bd0738dde6d4ac`  
> **WSGM pinned toolkit reference:** `KillerPixelCrew/steam-ui-toolkit` at `13ce887fef7828ab56dd065b545b4747e6131880`  
> **Scope:** make every Addon-initiated Steam Quick Access open enter the single Addon top-level tab, without changing SteamDeck Quick Access pulse ownership or inventing a guessed QAM-open lifecycle.

---

## 1. Goal

When the Addon's configured front-button action requests:

```text
FrontButtonAction.SteamQuickAccess
```

and Steam Quick Access is currently closed, the resulting QAM must open on:

```text
steam-input-addon
```

instead of Steam's remembered last-selected native top-level tab.

Preserve the existing native toggle behavior:

```text
QAM closed
→ SteamQuickAccess action
→ open QAM
→ select Addon once

QAM already open
→ SteamQuickAccess action
→ close QAM normally
→ do not arm/reselect Addon
```

Do not replace the current virtual SteamDeck Quick Access system-button pulse with a CEF-only open/close implementation.

---

## 2. Why this work is needed

Current production `qam.js` already contains a native top-level selection write:

```javascript
menuStore.OpenQuickAccessMenu(ADDON_TAB_KEY, false);
```

and the intended one-shot flow:

```text
qamSurfaceActive
qamInitialSelectionRequested
trySelectAddonTabForFreshOpen()
```

but its fresh-open trigger currently depends on:

```javascript
updateQamSurfaceVisibility(args[0]?.visible);
```

The supported Windows GamepadUI hardware path has repeatedly shown that this signal does not provide the required open/close lifecycle.

The 2026-09-13 hardware investigation already established:

```text
QAM opens correctly
Addon descriptors render correctly
OpenQuickAccessMenu selection code exists

but

QAM surface activated.
QAM surface deactivated.
QAM open selection: Addon

are not reached during normal opens.
```

The latest 2026-09-19 hardware log again showed the Addon descriptor being installed without any corresponding fresh-open selection event.

Therefore:

```text
the selection write is not disproven
the args[0].visible lifecycle authority is disproven
```

This work must replace the bad open-detection authority, not add another guessed visibility heuristic.

---

## 3. Required reading before editing

Read together:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md

docs/QAM_SINGLE_ADDON_TAB_NATIVE_INNER_TABS_RESEARCH_AND_DESIGN_2026-09-13.md

docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md
docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md

docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR2_QAM_FIVE_TAB_NATIVE_SHELL_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR5_CONVERGENCE_CLEANUP_PARITY_ACCEPTANCE_WORK_ORDER.md
```

Then inspect current production source:

```text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/CenterM/FrontButtonActionExecutor.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs

src/SteamInputAddonforClaw.QamHost/QamFrontendBridge.cs
src/SteamInputAddonforClaw.QamHost/Program.cs
src/SteamInputAddonforClaw.QamHost/Frontend/qam.js

tests/SteamInputAddonforClaw.Tests/FrontButtonDispatchTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
```

Also inspect the reviewed external reference:

```text
KillerPixelCrew/WSGM branch 2.0

src/WSGM/Core/Steam.cs
docs/steam-cef-system.md
docs/steam-cef.md
src/WSGM/Core/SteamUiAssets/NativeQamBootstrap.js

external/steam-ui-toolkit
pinned commit:
13ce887fef7828ab56dd065b545b4747e6131880
```

Do not add WSGM or `steam-ui-toolkit` as a dependency.

---

## 4. What WSGM 2.0 proves — and what it does not

WSGM 2.0 is useful architecture evidence, but it does not implement our exact custom-tab-first-open requirement.

### 4.1 WSGM keeps Steam QAM opening as a native Steam action

WSGM defines:

```csharp
public enum BigPictureShortcut
{
    SteamMenu,
    QuickAccess,
}
```

and maps Quick Access to Steam's own installed-client shortcut:

```text
Ctrl+2
```

Its code intentionally sends that input globally:

```csharp
public static bool TrySendBigPictureShortcut(BigPictureShortcut shortcut)
```

with the explicit product rule that the foreground game may remain foreground while Steam opens its own menu above it.

Important lesson:

```text
Steam owns menu open/close semantics
WSGM asks Steam to perform the native action
CEF patching does not replace that action
```

This matches our current product better than a direct CEF replacement would.

Our equivalent native opening authority is:

```text
SteamDeck Quick Access system-button pulse
```

Keep it.

### 4.2 WSGM separates UI patch ownership from menu-open authority

WSGM's CEF system patches/reuses Steam native UI surfaces.

Its current 2.0 native QAM work:

- semantically resolves native Steam surfaces;
- wraps exact matched panel roots;
- fails closed when a unique target cannot be proven;
- retains Steam's normal menu invocation path;
- does not infer a new product action merely because a renderer prop resembles visibility.

That supports this separation:

```text
Runtime/controller action
→ requests Steam's native QAM action

QamHost/CEF
→ changes only what QAM displays/selects
```

### 4.3 WSGM does not solve our custom Addon top-level selection

WSGM 2.0 primarily inserts controls into existing Steam-native Quick Settings / Performance surfaces.

It does not create:

```text
custom Addon top-level QAM descriptor
→ force custom descriptor selected on fresh open
```

Therefore do not claim this work is copied from WSGM.

The correct conclusion is narrower:

> WSGM reinforces keeping the native QAM opening authority independent from Steam UI patch ownership. Our extra one-shot selection requirement must be implemented in our own QamHost path.

### 4.4 WSGM's reliability rules still apply conceptually

Retain the same broad discipline visible in WSGM/toolkit:

```text
one proven native authority
exact/semantic target selection
bounded local patch ownership
explicit cleanup
fail open when optional UI augmentation is unavailable
no guessed broad lifecycle state machine
```

---

## 5. Current Steam Addon execution path

Current production action:

```text
physical front button
→ MsiClawFrontButtonRuntime
→ FrontButtonActionExecutor
→ FrontButtonAction.SteamQuickAccess
→ _tryRequestQuickAccessPulse()
→ IMsiClawAddonPresentation.TryRequestQuickAccessPulse()
→ current SteamDeck presentation system-button overlay
→ Steam receives Quick Access pulse
→ QAM toggles
```

This path is already the correct Steam/controller authority.

Do not replace it.

---

## 6. Target architecture

Use one causality-based one-shot intent:

```text
SteamQuickAccess action
        │
        v
Runtime Quick Access coordinator
        │
        ├─ best-effort tell connected QamHost:
        │      "the next QAM open caused by this action
        │       should select Addon"
        │
        └─ then issue existing SteamDeck Quick Access pulse
                 │
                 v
             Steam toggles QAM
                 │
                 v
         QAM tab array / Addon descriptor ready
                 │
                 v
      consume one pending Addon-selection request
                 │
                 v
 MenuStore.OpenQuickAccessMenu(ADDON_TAB_KEY, false)
                 │
                 v
        Addon top-level tab selected
```

This is not a general QAM lifecycle state machine.

It is one causal request tied to one known user action.

---

## 7. Do not use `args[0].visible` as authority

Remove the fresh-open selection dependency on:

```javascript
args[0]?.visible
```

for Addon first-tab selection.

The following existing concepts should be deleted if they are only retained for this failed mechanism:

```text
qamSurfaceActive
qamInitialSelectionRequested
updateQamSurfaceVisibility()
activateQamSurface()
deactivateQamSurface()
```

Do not replace them with guesses such as:

```text
isVisible
open
isOpen
active
focused
DOM visibility
MutationObserver
interval polling
timeout retries
focus heuristics
```

If another unrelated QAM behavior still depends on any of these names at implementation time, retain only that proven behavior and remove the selection dependency.

---

## 8. Add one narrow Runtime → QamHost notification

Reuse the existing:

```text
FrontendPipeEndpoint.CreateQamForCurrentUser()
```

connection.

Do not create:

- another named pipe;
- another QAM IPC server;
- a generic event bus;
- a UI command manager;
- a QAM session manager.

Add one explicit frontend notification kind, for example:

```csharp
internal enum FrontendNotificationKind
{
    StateInvalidated,
    CloseRequested,
    SelectAddonOnNextQuickAccessOpenRequested,
}
```

Exact naming may be adjusted for repository conventions, but the semantic meaning must stay narrow.

Because this changes the frontend wire contract, bump:

```text
FrontendTransportProtocol
v33 → v34
```

Add the corresponding version comment.

Overlay protocol is unaffected.

---

## 9. NamedPipeAddonFrontendServer — add a narrow send seam

Current server already has a single currently-served connection and a narrow `CloseRequested` notification.

Extend that existing model with one method dedicated to sending the QAM selection intent to the currently connected client.

Suggested shape:

```csharp
public async Task<bool> RequestSelectAddonOnNextQuickAccessOpenAsync(
    CancellationToken cancellationToken = default)
{
    ObjectDisposedException.ThrowIf(_disposed != 0, this);

    var served = _servedConnection;
    if (served is null)
        return false;

    try
    {
        await served.SendSelectAddonOnNextQuickAccessOpenRequestedAsync()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
    catch
    {
        return false;
    }
}
```

and extend `ServedConnection` with the corresponding send delegate.

The server must not:

- wait for QAM to open;
- retry;
- hold a timer;
- retain a QAM-open state;
- inspect Steam state.

Its only responsibility is:

```text
connected QamHost?
→ send one notification

not connected / send failed?
→ return false
```

---

## 10. NamedPipeAddonFrontendClient — expose one event

Add a matching client event, for example:

```csharp
public event EventHandler? SelectAddonOnNextQuickAccessOpenRequested;
```

In `ReadLoopAsync()`:

```csharp
if (message.Kind == FrontendWireMessageKind.Notification
    && message.Notification ==
       FrontendNotificationKind.SelectAddonOnNextQuickAccessOpenRequested)
{
    SelectAddonOnNextQuickAccessOpenRequested?.Invoke(this, EventArgs.Empty);
    continue;
}
```

Do not add payload data.

The target key is already fixed in QAM JS:

```text
steam-input-addon
```

No Runtime-supplied key is needed.

---

## 11. QamFrontendBridge — forward the intent as a dedicated event

Subscribe the bridge to the new client notification.

Suggested shape:

```csharp
internal event EventHandler? SelectAddonOnNextQuickAccessOpenRequested;

private void OnSelectAddonOnNextQuickAccessOpenRequested(
    object? sender,
    EventArgs e)
{
    SelectAddonOnNextQuickAccessOpenRequested?.Invoke(this, e);
}
```

Subscribe in the constructor and unsubscribe in `DisposeAsync()`.

Do not overload `StateInvalidated`.

A product-state invalidation and a one-shot UI action intent are different semantics.

---

## 12. QamHost Program — deliver the one-shot JS notification

Follow the existing C# → JS invalidation delivery pattern.

Add a dedicated delivery such as:

```csharp
async Task DeliverSelectAddonOnNextQuickAccessOpenAsync()
{
    try
    {
        await sessionClient.EvaluateAsync(
            "window.__STEAM_INPUT_ADDON_QAM__?.__receiveBridgeNotification?.('select-addon-on-next-open')",
            lifetimeToken);
    }
    catch (Exception exception)
    {
        log.Info(
            $"QAM Addon first-tab request delivery skipped for retired CDP session. {exception.Message}");
    }
}
```

Use the active current document/session only.

Do not retain the notification across a retired CDP document in C#.

If the current CDP session is gone, fail open and let the existing Quick Access pulse continue.

---

## 13. Runtime action sequencing

This is the most important ordering rule.

Wrong:

```text
fire notification task
fire SteamDeck Quick Access pulse immediately
hope notification arrives first
```

That reintroduces a timing race.

Required:

```text
known SteamQuickAccess user action
→ attempt one QamHost notification write
→ when that attempt completes
→ issue existing Quick Access pulse
```

The input callback must not block waiting on pipe I/O.

Use the smallest existing Runtime-owned asynchronous seam to serialize these two operations off the input callback.

A narrow private method on `AddonProcessHost` is preferred over introducing a new service/manager.

Illustrative shape:

```csharp
private void RequestSteamQuickAccess()
{
    _ = RequestSteamQuickAccessAsync();
}

private async Task RequestSteamQuickAccessAsync()
{
    try
    {
        if (_qamFrontendServer is not null)
        {
            _ = await _qamFrontendServer
                .RequestSelectAddonOnNextQuickAccessOpenAsync(
                    _startupCancellationTokenSource.Token)
                .ConfigureAwait(false);
        }
    }
    catch (OperationCanceledException)
    {
        // shutdown
    }
    catch (Exception exception)
    {
        AppLog.Debug(
            "QAM",
            "Addon first-tab intent could not be delivered; using native Quick Access pulse normally.",
            exception);
    }
    finally
    {
        _presentationOwnership?.TryRequestQuickAccessPulse();
    }
}
```

Do not mechanically copy this exact code if shutdown conventions differ in current source.

Important contract:

```text
notification unavailable
→ pulse still happens

QamHost disconnected
→ pulse still happens

QAM augmentation broken
→ pulse still happens

selection is optional
native Quick Access toggle is not
```

---

## 14. Avoid overlapping Quick Access coordinators

Do not add a queue, epoch, command processor, manager, or generalized action scheduler.

Normal handheld usage produces discrete front-button actions.

However, do not allow multiple fire-and-forget coordination tasks to reorder:

```text
press A
notification A
press B
notification B
pulse B
pulse A
```

Use the smallest practical serialization already consistent with repository style.

Acceptable examples:

- one `SemaphoreSlim(1,1)` scoped specifically to this Runtime Quick Access coordination method; or
- reuse an existing action serialization seam if one already guarantees front-button action ordering.

Do not introduce a generic front-button state machine.

The reason for this gate is concrete: notification must correspond to the pulse immediately following it.

---

## 15. QAM JS one-shot state

Replace inferred visibility state with one explicit pending intent:

```javascript
state.selectAddonOnNextOpenRequested = false;
```

When receiving:

```text
select-addon-on-next-open
```

set:

```javascript
state.selectAddonOnNextOpenRequested = true;
```

No count, epoch, timestamp, owner id, retry budget, or timer is required.

One pending bit is enough for the supported product action.

---

## 16. Distinguish open from close using Steam's actual menu authority

The same SteamQuickAccess action toggles:

```text
closed → open
open → close
```

The one-shot Addon selection must be armed only for an open.

Do not infer this from QAM renderer visibility.

Use Steam's actual menu state when the request reaches QamHost/JS.

Current Steam runtime evidence exposes:

```text
GamepadUIMainWindowInstance
  .m_MenuStore
  .m_eOpenSideMenu
```

with Quick Access represented by the native Quick Access side-menu state.

Resolve the exact current store path fail-closed.

The existing `resolveNativeTabSelection()` currently uses:

```javascript
window.SteamUIStore
  ?.m_WindowStore
  ?.m_Parent
  ?.m_WindowStore
  ?.MainWindowInstance
  ?.MenuStore
```

Before implementation, live-inspect which current WindowStore/MainWindow instance owns both:

```text
OpenQuickAccessMenu
m_eOpenSideMenu or equivalent current side-menu state
```

Do not maintain two guessed store paths if one current verified authority contains both.

Preferred helper:

```javascript
function resolveNativeQamMenuAuthority() {
  const menuStore = /* one verified current Steam path */;

  if (!menuStore ||
      typeof menuStore.OpenQuickAccessMenu !== "function")
    return null;

  return {
    isQuickAccessOpen: () =>
      menuStore.m_eOpenSideMenu === QUICK_ACCESS_SIDE_MENU_ID,

    selectAddon: () =>
      menuStore.OpenQuickAccessMenu(ADDON_TAB_KEY, false),
  };
}
```

The exact integer/value for Quick Access must be verified against the current supported Steam build before hard-coding.

If current source provides a stable enum/member instead, use that.

---

## 17. Arm only when QAM is currently closed

On JS notification:

```text
native menu authority unavailable
→ do not arm
→ log once / fail open

QAM already open
→ do not arm
→ next pulse is expected to close QAM

QAM closed
→ set one pending selection bit
```

Illustrative shape:

```javascript
function requestAddonSelectionOnNextQuickAccessOpen() {
  const authority = resolveNativeQamMenuAuthority();
  if (!authority) {
    logOnce(
      "quickAccessMenuAuthorityUnavailable",
      "QAM Addon first-tab request ignored; native menu authority unavailable.");
    return;
  }

  if (authority.isQuickAccessOpen()) {
    state.selectAddonOnNextOpenRequested = false;
    return;
  }

  state.selectAddonOnNextOpenRequested = true;
}
```

Do not make this function open QAM itself.

The existing SteamDeck pulse remains responsible for open/close.

---

## 18. Consume only after the Addon descriptor is actually present

The custom key may not be selectable before Steam's tab array contains our descriptor.

Therefore consume in the already-proven descriptor insertion path:

```javascript
ensureAddonTabs(owner, React, native)
```

after the Addon descriptor is known to be in the current tab array.

Conceptual rule:

```javascript
function tryConsumeAddonSelectionRequest() {
  if (!state.selectAddonOnNextOpenRequested ||
      !state.addonTabDescriptor)
    return;

  const authority = resolveNativeQamMenuAuthority();
  if (!authority)
    return;

  state.selectAddonOnNextOpenRequested = false;

  try {
    authority.selectAddon();
    log("QAM open selection: Addon");
  } catch (error) {
    logOnce(
      "initialTabSelectionFailure",
      `QAM Addon first-tab selection failed: ${String(error)}`);
  }
}
```

Call after:

```text
descriptor built
tabs array contains descriptor
```

Do not repeatedly call it every render once the bit was consumed.

---

## 19. Cold-first-open descriptor ordering

This work intentionally handles the real ordering problem without retries.

Possible cold sequence:

```text
QamHost already connected
Addon descriptor not yet rendered in a QAM tab array

OEM1
→ Runtime sends one-shot intent
→ pulse opens QAM
→ Steam renders tab array
→ ensureAddonTabs inserts Addon
→ pending request consumed
→ OpenQuickAccessMenu(Addon)
```

This is the desired reason to consume from `ensureAddonTabs()`, not from a timer immediately after receiving the Runtime notification.

Do not add:

- setTimeout;
- repeated selection attempts;
- polling for descriptor presence.

The render that creates the descriptor is already the natural event.

---

## 20. Pending intent lifecycle

Clear the pending bit on:

```text
successful or attempted consumption
uninstall()
new script install/reinstall
document/session replacement
bridge teardown
```

A stale request must not select Addon on some unrelated later QAM open after the original Runtime action was abandoned by a Steam/QamHost session replacement.

No persistent storage.

No Runtime setting.

No shell contract change.

---

## 21. Keep native QAM toggle authority unchanged

Do not replace:

```csharp
IMsiClawAddonPresentation.TryRequestQuickAccessPulse()
```

with:

```javascript
OpenQuickAccessMenu(...)
OpenSideMenu(...)
DOM click
synthetic JS input
```

The system-button pulse remains the one product path that already owns:

- SteamDeck presentation eligibility;
- Steam-native Quick Access behavior;
- open/close toggle semantics;
- game-overlay integration.

The new CEF notification only controls:

```text
which top-level QAM tab is selected after a Runtime-caused open
```

---

## 22. Preserve manual Steam QAM behavior

Do not force Addon selection when the user opens QAM through a source the Addon did not request.

Examples:

- another physical Steam controller;
- Steam's own keyboard shortcut;
- another Steam UI navigation path.

This work is scoped to the Addon's known:

```text
FrontButtonAction.SteamQuickAccess
```

action.

Why:

```text
known Addon action
→ deterministic causality
→ safe one-shot override

unknown external open
→ no causality
→ preserve Steam behavior
```

Do not build global QAM-open monitoring merely to catch every possible external source.

---

## 23. Current inner-tab selection remains separate

Top-level rule:

```text
Addon-initiated fresh QAM open
→ Addon top-level tab
```

Inner five-tab selection remains surface-local presentation state and follows the current shared shell behavior.

Do not couple:

```text
Device/Profile/Controller/Shortcut/Setting
```

selection to this work.

Do not use active AppId to choose a top-level tab.

There is exactly one Addon top-level key:

```text
steam-input-addon
```

---

## 24. Failure policy

### QamHost not connected

```text
selection intent send fails
→ log at Debug/Info as appropriate
→ still pulse Quick Access
```

### Steam native menu authority unavailable

```text
notification reaches QamHost
→ JS cannot resolve exact authority
→ do not arm/select
→ Steam QAM still opens normally through pulse
```

### Addon descriptor cannot be inserted

```text
pending request never consumed in that current document
→ QAM remains usable with native tabs
→ pending retires on document/session replacement/uninstall
```

### Selection write throws

```text
consume pending request once
→ log
→ do not retry-loop
→ QAM remains open/usable
```

The optional selection enhancement must never break the core Quick Access button.

---

## 25. No Full1902 controller ownership changes

Do not modify:

- PID1901 / PID1902 authority;
- DirectInput ownership;
- HidHide reconciliation;
- VIIPER attach/detach;
- X360 vs SteamDeck presentation policy;
- suspend/resume presentation handling;
- physical-device recovery;
- Center M authority transitions;
- rumble;
- Overlay capture.

The only presentation-owner call involved is the already-existing:

```text
TryRequestQuickAccessPulse()
```

---

## 26. Required tests — frontend transport

Update `FrontendNamedPipeTransportTests.cs`.

Cover:

### Notification delivery

```text
connected client
→ server RequestSelectAddonOnNextQuickAccessOpenAsync()
→ client event raised exactly once
```

### No connected client

```text
no served connection
→ request returns false
→ no exception
```

### Protocol version

Assert current frontend protocol:

```text
34
```

and existing mismatch behavior remains fail-closed.

### Existing notifications

Ensure:

```text
StateInvalidated
CloseRequested
```

still work independently.

Do not turn notification tests into a generic messaging framework.

---

## 27. Required tests — QamFrontendBridge / Program seam

Update bridge tests to prove:

```text
client notification
→ QamFrontendBridge dedicated event
```

Add the smallest practical Program/session-delivery test if current test seams support it.

At minimum, source/contract tests must prove the JS notification string is:

```text
select-addon-on-next-open
```

or the chosen stable equivalent.

Do not overload `state-invalidated`.

---

## 28. Required tests — qam.js contract

Update `QamFrontendContractTests.cs`.

Guard all of the following:

### Old visibility authority removed

The first-tab selection flow must no longer depend on:

```javascript
updateQamSurfaceVisibility(args[0]?.visible)
```

or the old:

```text
qamSurfaceActive
qamInitialSelectionRequested
```

state.

### Dedicated notification accepted

```javascript
__receiveBridgeNotification
```

must dispatch:

```text
select-addon-on-next-open
```

to a narrow handler.

### One pending bit

Guard the intended state shape, e.g.:

```javascript
state.selectAddonOnNextOpenRequested
```

Do not require exact naming if implementation uses a clearer equivalent.

### Current menu state checked before arming

Assert the selection request does not blindly arm while QAM is already open.

### Descriptor readiness before consumption

Assert consumption occurs only after:

```text
state.addonTabDescriptor
```

exists / current tab augmentation has run.

### Native selection still uses

```javascript
OpenQuickAccessMenu(ADDON_TAB_KEY, false)
```

Do not replace it with DOM interaction.

---

## 29. Required tests — Runtime ordering

Add focused Runtime/unit coverage around the new coordination seam.

Prove:

```text
notification attempt starts/completes
BEFORE
TryRequestQuickAccessPulse()
```

for a normal connected-QamHost path.

Prove:

```text
notification fails
→ Quick Access pulse still invoked exactly once
```

Prove:

```text
notification unavailable
→ Quick Access pulse still invoked exactly once
```

If a small per-action serialization gate is added, prove two requests cannot reorder notification/pulse pairs.

Do not test pathological instruction-level interleavings.

---

## 30. Logging

Keep logging concise and useful for hardware validation.

Recommended Runtime logs:

```text
QAM Addon first-tab intent delivered.
QAM Addon first-tab intent unavailable; native Quick Access pulse continues.
```

Recommended QamHost/JS logs:

```text
QAM Addon first-tab request armed.
QAM Addon first-tab request ignored; Quick Access already open.
QAM open selection: Addon
```

Do not spam each render.

Use `logOnce` for static discovery failures.

The successful one-shot selection may log once per user action.

---

## 31. Real-device acceptance

Use the current MSI Claw hardware on supported Steam/BPM operation.

### Case A — QAM closed, last Steam native tab remembered

1. Open QAM.
2. Select a Steam-native top-level tab.
3. Close QAM.
4. Press the Addon-mapped `SteamQuickAccess` front button.

Expected:

```text
QAM opens
Addon top-level tab is selected
inner Addon Quick Settings renders
```

### Case B — QAM closed, Addon was already last selected

Press `SteamQuickAccess`.

Expected:

```text
QAM opens normally
Addon selected
no double navigation/flicker loop
```

### Case C — QAM already open

Press `SteamQuickAccess`.

Expected:

```text
QAM closes
no forced Addon reselection
no immediate reopen
```

### Case D — reopen after Case C

Press again.

Expected:

```text
QAM opens
Addon selected
```

### Case E — QamHost unavailable / killed

Press `SteamQuickAccess`.

Expected:

```text
QAM native toggle remains usable through SteamDeck pulse
Addon-first enhancement may be absent
controller behavior remains correct
```

### Case F — manual/non-Addon QAM open

Open QAM through a non-Addon source if available.

Expected:

```text
Steam's normal remembered selection behavior is preserved
Addon does not globally hijack every external QAM open
```

### Case G — repeated normal presses

Perform several ordinary open/close cycles.

Expected:

```text
one press = one native toggle
each Addon-caused open starts on Addon
no duplicate pulse
no stuck pending request
```

Do not manufacture extreme timing sequences solely for testing.

---

## 32. Acceptance criteria

This work is complete when all are true:

1. Addon-initiated QAM opens select `steam-input-addon` on hardware.
2. QAM close via the same front button remains native Steam toggle behavior.
3. The existing SteamDeck Quick Access pulse remains the open/close authority.
4. `args[0].visible` is no longer the authority for first-tab selection.
5. No replacement guessed QAM visibility heuristic is introduced.
6. Runtime sends one narrow QamHost intent before issuing the corresponding pulse.
7. QamHost absence/failure never prevents the pulse.
8. QAM checks current native menu state and does not arm a selection for a close action.
9. Selection is consumed only after the Addon descriptor is present.
10. One request produces at most one `OpenQuickAccessMenu(ADDON_TAB_KEY, false)` selection attempt.
11. Frontend protocol is bumped from v33 to v34.
12. Overlay protocol is unchanged.
13. Manual/external QAM opens are not globally hijacked.
14. Full1902 controller ownership/lifecycle code outside the narrow action path remains unchanged.
15. Existing Device/Profile/Controller/Shortcut/Setting shared-surface behavior remains unchanged.
16. All automated tests pass.
17. Hardware validation passes Cases A-G above.

---

## 33. Review guidance

Treat this as a focused product-behavior fix, not an invitation to redesign QAM lifecycle.

### Blocking

Examples:

- selection still depends on guessed renderer visibility;
- pulse can be skipped because QamHost notification failed;
- a close action gets incorrectly armed as an open;
- notification/pulse ordering is not causal;
- stale pending selection survives document/session replacement;
- repeated render causes repeated selection writes;
- native Steam QAM open/close is replaced unnecessarily;
- protocol changed without version bump;
- unrelated Full1902 controller lifecycle is modified.

### Non-blocking

Examples:

- naming preference for the one notification/event;
- log wording;
- minor local helper extraction.

### Theoretical / do not block

Do not require epochs, barriers, multi-generation request ids, generalized queues, or additional state machines for instruction-level interleavings not demonstrated in supported handheld lifecycle.

The supported product invariant is simple:

```text
known Addon SteamQuickAccess action
→ one selection intent
→ one native pulse
→ if that pulse opens QAM, select Addon once
→ if it closes QAM, do not select
```

Keep the implementation proportional to that invariant.
