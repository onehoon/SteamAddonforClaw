# Work Order — Per-Shortcut Close-Overlay-After-Launch Policy

> **Date:** 2026-10-07  
> **Repository:** `onehoon/SteamAddonforClaw`  
> **Reviewed main:** `4a675f805d0431a2ea3c14fad5ee02a73d858a2e`  
> **Product baseline:** standalone Full1902  
> **Distribution state:** pre-release; there is no deployed Shortcut persistence compatibility requirement  
> **Primary authority:** `docs/Full 1902 Implementation/README.md` and `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`  
> **Shortcut authority:** current Shortcut Foundation + `docs/work-order/1007_SHORTCUT_3_COLUMN_BUILTIN_ACTIONS_WORK_ORDER.md`  
> **Scope:** add one persisted per-Shortcut option that closes the Addon Overlay after a successful launch, default it ON for Steam / Steam Big Picture / Xbox, keep Screenshot's existing mandatory hide-before-capture behavior, and reuse the existing Runtime-owned Overlay retirement path.  
> **Implementation shape:** one focused PR.

---

## 1. Goal

When the user launches selected Shortcut actions from the Addon Overlay, the launched destination normally becomes the next place the user wants to interact with.

The built-in actions:

~~~text
Steam
Steam Big Picture
Xbox
~~~

should therefore default to:

~~~text
execute successfully
→ close/retire the Addon Overlay
→ resume the current Full1902 presentation safely
~~~

This behavior must also be a per-Shortcut user preference so any ordinary launch Shortcut can opt in or out.

Target editor behavior:

~~~text
Steam Big Picture
Close Overlay after launch                      [ON]

Steam
Close Overlay after launch                      [ON]

Xbox
Close Overlay after launch                      [ON]

Application (.exe)
Close Overlay after launch                      [OFF]

PowerShell
Close Overlay after launch                      [OFF]

Website (URL)
Close Overlay after launch                      [OFF]
~~~

Screenshot remains special:

~~~text
Screenshot
→ Overlay retirement is mandatory BEFORE capture
→ no user OFF switch
~~~

Do not auto-create Steam / Steam Big Picture / Xbox Shortcut tiles. "Default ON" refers only to the new close-after-launch preference when a new Shortcut of those action types is created.

---

## 2. Product behavior

### 2.1 Normal launch Shortcut with close OFF

~~~text
Overlay visible/captured
→ user activates Shortcut
→ Runtime resolves TileId
→ action launches successfully
→ CloseOverlayAfterLaunch == false
→ Overlay stays visible/captured
~~~

### 2.2 Normal launch Shortcut with close ON

~~~text
Overlay visible/captured
→ user activates Shortcut
→ Runtime resolves TileId
→ action launches successfully
→ CloseOverlayAfterLaunch == true
→ Runtime retires Overlay through the existing OQ4 retirement path
→ consumed controls release
→ same current X360/SteamDeck presentation resumes
~~~

### 2.3 Launch failure

~~~text
action launch fails
→ do NOT close Overlay
→ return existing Shortcut failure
~~~

The user must not lose the Overlay because the requested destination failed to launch.

### 2.4 Screenshot

Keep the existing sequence:

~~~text
Overlay visible/captured
→ user activates Screenshot
→ Runtime retires Overlay first
→ retirement proven
→ capture primary display
~~~

Screenshot is not converted to "capture first, close later".

---

## 3. Full1902 boundaries

This work must not change:

- PID1901 / PID1902 ownership;
- DirectInput physical ownership;
- HidHide authority;
- VIIPER ownership/teardown;
- Xbox360 / SteamDeck presentation selection policy;
- Steam/BPM detection;
- XBOX active-game detection;
- sleep / hibernate / resume ownership;
- restart / crash / shutdown recovery;
- WING / Game Bar suppression;
- controller publisher ownership.

Overlay close must reuse the existing unified retirement path:

~~~text
RetireOverlayCaptureUnderTransitionAsync(...)
~~~

Do not add:

~~~text
ShortcutOverlayCloser
ShortcutDismissManager
OverlayLaunchCoordinator
new presentation owner
new controller gate
new lifecycle state machine
~~~

The current Runtime remains the sole visibility/capture authority.

---

## 4. Current implementation facts

Current persisted Shortcut shape:

~~~csharp
public sealed record ShortcutTileDefinition(
    Guid TileId,
    string Title,
    ShortcutActionSpec Action);
~~~

Current root document:

~~~csharp
public sealed record ShortcutDocument
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public ShortcutDashboardDefinition Dashboard { get; init; } = ShortcutDashboardDefinition.Empty;
}
~~~

Current Overlay execution contract remains intentionally narrow:

~~~text
Overlay
→ TileId only
→ Runtime ShortcutRuntime.ExecuteAsync(TileId)
~~~

Current built-ins:

~~~text
system.steam-big-picture
→ steam://open/bigpicture

system.steam-client
→ steam://open/main

system.xbox-app
→ explorer.exe shell:AppsFolder\{XboxGamingHomeAppIdentity.Aumid}

system.screenshot-fullscreen
→ Runtime-owned screenshot callback
~~~

Current Screenshot callback already owns a safe retirement sequence:

~~~text
_visibleSurfaceTransition
→ RetireOverlayCaptureUnderTransitionAsync("ShortcutScreenshot")
→ capture
~~~

Current ordinary Overlay Shortcut execution does not retire the Overlay:

~~~csharp
var result = await _shortcutRuntime.ExecuteAsync(tileId, token).ConfigureAwait(false);
return new(result.Outcome == ShortcutExecutionOutcome.Succeeded, result.FailureMessage);
~~~

This PR adds only the missing per-tile policy between successful execution and the existing retirement seam.

---

## 5. Persist the preference at tile level

Add one explicit required Boolean to the persisted tile:

~~~csharp
public sealed record ShortcutTileDefinition(
    Guid TileId,
    string Title,
    bool CloseOverlayAfterLaunch,
    ShortcutActionSpec Action);
~~~

This is a tile preference, not an action parameter.

Do **not** put it inside:

~~~text
ShortcutActionSpec.Parameters
~~~

Reason:

~~~text
Action.Parameters
→ describes how the action executes

CloseOverlayAfterLaunch
→ describes what the Addon Overlay does after successful execution
~~~

The Steam / Xbox built-ins remain parameterless schema-1 actions with:

~~~json
{}
~~~

Do not add URI/AUMID/close flags to those parameter objects.

---

## 6. No migration or legacy compatibility

The product is pre-release.

Do not add:

- nullable fallback fields;
- implicit action-derived fallback for missing persisted data;
- schema-1-to-schema-2 migration;
- old/new dual reader;
- legacy tile repair;
- automatic rewrite of an old Shortcut document;
- compatibility shims.

Bump the Shortcut **root document** schema:

~~~csharp
ShortcutDocument.CurrentSchemaVersion = 2;
~~~

The action-family schema remains:

~~~text
SupportedActionSchemaVersion = 1
~~~

These are different version domains.

### 6.1 Strict root schema admission

Update `ShortcutStore.Load()` so only the current root schema is admitted.

Required:

~~~text
schemaVersion == ShortcutDocument.CurrentSchemaVersion
→ load

schemaVersion != ShortcutDocument.CurrentSchemaVersion
→ Shortcut Runtime unavailable for that document
→ preserve original file
→ no migration/rewrite
~~~

The existing feature-local failure policy remains:

~~~text
bad/unsupported shortcuts.json
→ Shortcut feature unavailable
→ rest of Addon Runtime continues
~~~

It is acceptable that an old development schema-1 Shortcut file must be deleted/recreated manually.

Update log wording so it no longer claims only "newer than this build" when an older schema is also unsupported.

---

## 7. Main App frontend contract

The Main App editor must read and write the persisted Boolean.

Update:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutEditorFrontendContracts.cs
~~~

### 7.1 Editor projection

Add the explicit value to:

~~~csharp
public sealed record FrontendShortcutEditorTile(
    Guid TileId,
    string Title,
    string TargetSummary,
    bool CloseOverlayAfterLaunch,
    FrontendShortcutEditorAction Action);
~~~

The exact parameter position may follow repository style, but it must be a non-null Boolean.

### 7.2 Mutation intent

Create/Update must carry the tile preference separately from Action.

Recommended narrow shape:

~~~csharp
public sealed record FrontendShortcutMutationIntent(
    FrontendShortcutMutationKind Kind,
    Guid? TileId = null,
    string? Title = null,
    FrontendShortcutActionInput? Action = null,
    bool? CloseOverlayAfterLaunch = null,
    int? TargetIndex = null);
~~~

The nullable request field is for closed mutation validation only:

~~~text
Create / Update
→ CloseOverlayAfterLaunch MUST be present

Delete / Move
→ CloseOverlayAfterLaunch MUST be absent
~~~

This is not persisted nullable compatibility state.

Persisted `ShortcutTileDefinition.CloseOverlayAfterLaunch` remains plain `bool`.

---

## 8. Frontend protocol bump

The Main UI frontend contract changes.

Current reviewed version:

~~~csharp
FrontendTransportProtocol.CurrentVersion = 66;
~~~

Bump it to the next version and document:

~~~text
Version 67:
Shortcut editor tile/mutation contracts add the required
CloseOverlayAfterLaunch tile preference.
~~~

Do not change the Overlay transport protocol.

The Overlay still receives only the existing dashboard projection:

~~~text
TileId
Title
StatusText
State
Enabled
~~~

and still sends only:

~~~text
TileId
~~~

No close preference or ActionType needs to cross the Overlay wire.

---

## 9. Runtime editor projection/mutation

Update:

~~~text
src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
~~~

### 9.1 Projection

`ProjectEditorSnapshot(...)` must project:

~~~text
tile.CloseOverlayAfterLaunch
~~~

into the Main App editor DTO.

Do not add this field to `FrontendShortcutTile`, because the Overlay does not need it.

### 9.2 Create

Create must require:

~~~text
Title
Action
CloseOverlayAfterLaunch
~~~

and persist:

~~~csharp
new ShortcutTileDefinition(
    Guid.NewGuid(),
    intent.Title,
    intent.CloseOverlayAfterLaunch.Value,
    action)
~~~

### 9.3 Update

Update must require the same Boolean and replace the persisted preference together with Title/Action.

Conceptually:

~~~csharp
tiles[index] = tiles[index] with
{
    Title = intent.Title,
    CloseOverlayAfterLaunch = intent.CloseOverlayAfterLaunch.Value,
    Action = action!
};
~~~

### 9.4 Delete / Move

Delete and Move must reject unexpected close-option payloads just as they reject unrelated Title/Action payloads.

Keep mutation validation closed and exact.

---

## 10. Main App editor UX

Update:

~~~text
src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
~~~

Add one normal toggle to the Add/Edit Shortcut dialog:

~~~text
Close Overlay after launch                       [ON/OFF]
~~~

Use a standard WinUI `ToggleSwitch`.

Do not add a separate settings page.

Do not put the option on the Overlay tile itself.

### 10.1 Creation defaults

Add one helper equivalent to:

~~~csharp
internal static bool DefaultCloseOverlayAfterLaunch(
    FrontendShortcutEditorActionKind kind) =>
    kind is
        FrontendShortcutEditorActionKind.SteamBigPicture or
        FrontendShortcutEditorActionKind.SteamClient or
        FrontendShortcutEditorActionKind.XboxApp;
~~~

New Shortcut defaults:

~~~text
Steam Big Picture   true
Steam               true
Xbox                true

Executable          false
PowerShell          false
Website             false
~~~

When creating a new Shortcut and changing the action picker, apply the selected action's creation default.

This is UI creation behavior only.

Do not persist "default" as another state.

### 10.2 Editing existing tiles

When editing an existing Shortcut:

~~~text
toggle initial value = persisted CloseOverlayAfterLaunch
~~~

Opening the dialog must not overwrite the stored preference merely because the action picker initializes.

If the user changes the action type while editing an existing tile, preserve the current toggle choice unless the user changes it manually.

Do not create action-change heuristics or hidden default ownership for existing tiles.

### 10.3 Screenshot UX

Screenshot has a mandatory existing hide-before-capture lifecycle.

Therefore:

- hide/collapse the `Close Overlay after launch` toggle for Screenshot;
- persist `false` for the tile-level post-launch option when Screenshot is saved;
- keep Screenshot retirement behavior entirely in the existing screenshot callback.

Optional explanatory copy is acceptable:

~~~text
The Overlay is hidden before capture.
~~~

Do not expose a switch that can disable Screenshot retirement.

---

## 11. Runtime execution must remain TileId-owned

Do not send ActionType or close preference from Overlay.

Current authority remains:

~~~text
Overlay
→ TileId

ShortcutRuntime
→ resolves exact current ShortcutTileDefinition
→ validates current Action
→ executes current Action
→ knows current persisted CloseOverlayAfterLaunch
~~~

This prevents frontend state from becoming execution authority.

---

## 12. Carry only a narrow internal retirement directive

Extend the internal execution result, not the frontend/Overlay DTO.

Recommended:

~~~csharp
internal sealed record ShortcutExecutionResult(
    ShortcutExecutionOutcome Outcome,
    string? FailureMessage = null,
    bool RetireOverlayAfterExecution = false);
~~~

The property is Runtime-internal.

For non-Screenshot actions:

~~~text
execution failed
→ RetireOverlayAfterExecution = false

execution succeeded
+ tile.CloseOverlayAfterLaunch == false
→ false

execution succeeded
+ tile.CloseOverlayAfterLaunch == true
→ true
~~~

A simple implementation shape:

~~~csharp
var result = tile.Action.TypeId switch
{
    ...
};

return result.Outcome == ShortcutExecutionOutcome.Succeeded
    && tile.CloseOverlayAfterLaunch
    ? result with { RetireOverlayAfterExecution = true }
    : result;
~~~

Screenshot must not set this post-execution directive because it already performs mandatory retirement **before** capture.

Do not add another public execution DTO or callback solely for this Boolean.

---

## 13. Post-launch Overlay retirement belongs in AddonProcessHost

Update:

~~~text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
~~~

Current:

~~~csharp
private async Task<OverlayShortcutExecutionOutcome> HandleOverlayShortcutExecutionAsync(
    Guid tileId,
    CancellationToken token)
{
    if (Volatile.Read(ref _processShutdownStarted) != 0 || !_overlayCaptureActive)
        return new(false, "Shortcut is unavailable.");

    var result = await _shortcutRuntime.ExecuteAsync(tileId, token).ConfigureAwait(false);
    return new(result.Outcome == ShortcutExecutionOutcome.Succeeded, result.FailureMessage);
}
~~~

New high-level behavior:

~~~text
execute TileId
→ failed?
    yes → return failure, keep Overlay

→ succeeded and no retirement requested?
    yes → return success, keep Overlay

→ succeeded and retirement requested?
    acquire existing _visibleSurfaceTransition
    → if Overlay is already gone, success
    → otherwise RetireOverlayCaptureUnderTransitionAsync("ShortcutLaunch", false)
    → retirement proven: success
    → retirement not proven: report close failure, keep fail-safe capture/neutral state
~~~

Conceptual code:

~~~csharp
var result = await _shortcutRuntime.ExecuteAsync(tileId, token).ConfigureAwait(false);
if (result.Outcome != ShortcutExecutionOutcome.Succeeded
    || !result.RetireOverlayAfterExecution)
{
    return new(
        result.Outcome == ShortcutExecutionOutcome.Succeeded,
        result.FailureMessage);
}

await _visibleSurfaceTransition.WaitAsync(token).ConfigureAwait(false);
try
{
    if (!_overlayCaptureActive && !_overlayController.IsVisible)
        return new(true);

    var retired = await RetireOverlayCaptureUnderTransitionAsync(
        "ShortcutLaunch",
        surfaceAlreadyGone: false).ConfigureAwait(false);

    if (!retired)
    {
        AppLog.Warn(
            "Shortcuts",
            "Shortcut launched but Overlay retirement was not proven.",
            null,
            ("Event", "ShortcutPostLaunchOverlayRetirementIncomplete"));
        return new(false, "Shortcut launched, but the Overlay could not be closed.");
    }

    return new(true);
}
finally
{
    _visibleSurfaceTransition.Release();
}
~~~

Exact wording may follow project style.

Do not attempt to terminate/undo an already-launched external destination if Overlay retirement fails.

---

## 14. Why launch comes before close

For Steam / Steam Big Picture / Xbox and any optional close-enabled launch action:

~~~text
launch first
→ verify existing Shortcut execution succeeded
→ then retire Overlay
~~~

This is intentional.

Do not change them to:

~~~text
hide Overlay
→ launch
~~~

because a launch failure would unnecessarily dismiss the user's Shortcut surface.

Screenshot remains the intentional opposite because Overlay visibility would contaminate the capture.

---

## 15. Full1902 presentation behavior after Big Picture launch

Launching Steam Big Picture may update BPM state while the Overlay still owns capture.

Do not add Shortcut-specific presentation switching.

Correct ownership remains:

~~~text
Steam Big Picture launch
→ existing BPM watcher updates Runtime presentation desire
→ Overlay retirement uses existing Full1902 release/resume path
→ current authoritative presentation converges normally
~~~

Do not directly attach/detach Xbox360 or SteamDeck from Shortcut code.

The same rule applies to Steam/Xbox launch actions: Shortcut execution owns the external action only; controller presentation remains owned by existing Full1902 Runtime policy.

---

## 16. Do not modify Screenshot retirement

Keep:

~~~text
ExecuteFullscreenScreenshotShortcutAsync
→ _visibleSurfaceTransition
→ RetireOverlayCaptureUnderTransitionAsync("ShortcutScreenshot")
→ NirCmdScreenshotCapture
~~~

Do not funnel Screenshot into the new post-launch policy.

Do not make Screenshot's safety dependent on `CloseOverlayAfterLaunch`.

---

## 17. Expected production files

Expected changes:

~~~text
src/SteamInputAddonforClaw.Contracts/Shortcuts/ShortcutContracts.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutDocument.cs
src/SteamInputAddonforClaw/Shortcuts/ShortcutStore.cs

src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutEditorFrontendContracts.cs
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs

src/SteamInputAddonforClaw/Shortcuts/ShortcutRuntime.cs
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs

src/SteamInputAddonforClaw.UI/Views/ShortcutPage.xaml.cs
~~~

Tests will change accordingly.

Expected not to change:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/ShortcutFrontendContracts.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Shortcuts.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
PID1902/HidHide/VIIPER/publisher code
~~~

If implementation starts changing Overlay dashboard DTOs or controller ownership, stop and re-evaluate.

---

## 18. Persistence tests

Update:

~~~text
tests/SteamInputAddonforClaw.Tests/ShortcutStoreTests.cs
tests/SteamInputAddonforClaw.Tests/ShortcutFoundationContractTests.cs
~~~

Required:

1. `ShortcutDocument.CurrentSchemaVersion == 2`.
2. schema 2 with explicit `closeOverlayAfterLaunch` round-trips exactly.
3. schema 1 is rejected/preserved; no migration/rewrite.
4. future root schema is rejected/preserved.
5. malformed documents still fail feature-locally.
6. structural validation still rejects invalid tile/action identity.
7. collection order remains the only tile layout authority.
8. no Row/Column/X/Y/Order field is added.

Update all test constructors to supply the explicit Boolean.

Do not add migration tests.

---

## 19. Shortcut Runtime tests

Update:

~~~text
tests/SteamInputAddonforClaw.Tests/ShortcutRuntimeTests.cs
tests/SteamInputAddonforClaw.Tests/ShortcutEditorRuntimeTests.cs
~~~

Required execution cases:

### Close disabled

~~~text
valid executable/url/powershell/built-in
CloseOverlayAfterLaunch = false
execution succeeds
→ RetireOverlayAfterExecution = false
~~~

### Close enabled

~~~text
valid executable/url/powershell/built-in
CloseOverlayAfterLaunch = true
execution succeeds
→ RetireOverlayAfterExecution = true
~~~

At minimum explicitly cover all three default-on built-ins:

~~~text
Steam Big Picture
Steam
Xbox
~~~

### Launch failure

~~~text
CloseOverlayAfterLaunch = true
launch fails
→ RetireOverlayAfterExecution = false
~~~

### Screenshot

~~~text
CloseOverlayAfterLaunch persisted false
screenshot callback succeeds
→ existing callback path used
→ RetireOverlayAfterExecution = false
~~~

### Editor create/update

Prove:

~~~text
Create requires explicit close Boolean
Update requires explicit close Boolean
Delete/Move reject unexpected close Boolean
Projection returns exact persisted Boolean
~~~

Keep current action schema validation.

---

## 20. Main UI tests

Update:

~~~text
tests/SteamInputAddonforClaw.UiTests/UiArchitectureTests.cs
~~~

or the nearest existing Shortcut editor UI tests.

Required helpers/behavior:

~~~text
DefaultCloseOverlayAfterLaunch(SteamBigPicture) == true
DefaultCloseOverlayAfterLaunch(SteamClient) == true
DefaultCloseOverlayAfterLaunch(XboxApp) == true

DefaultCloseOverlayAfterLaunch(Executable) == false
DefaultCloseOverlayAfterLaunch(PowerShell) == false
DefaultCloseOverlayAfterLaunch(Url) == false
~~~

Prove:

- Create dialog applies the selected action default.
- Existing edit uses persisted value, not the action default.
- Screenshot hides the toggle.
- Screenshot save writes false for post-launch close.
- Create/Update mutation includes the selected Boolean.
- Existing default-title behavior remains unchanged.

Do not add another editor ViewModel solely for this toggle.

---

## 21. AddonProcessHost retirement tests

Update/add focused tests near:

~~~text
tests/SteamInputAddonforClaw.Tests/AddonProcessHostOverlayQuickSettingsContractTests.cs
tests/SteamInputAddonforClaw.Tests/AddonProcessHostScreenshotContractTests.cs
~~~

Required code/behavior proofs:

### Success + close false

~~~text
ShortcutRuntime returns Succeeded + RetireOverlayAfterExecution=false
→ no post-launch retirement
→ success
~~~

### Success + close true

~~~text
ShortcutRuntime returns Succeeded + RetireOverlayAfterExecution=true
→ acquire _visibleSurfaceTransition
→ RetireOverlayCaptureUnderTransitionAsync("ShortcutLaunch", false)
~~~

### Launch failure

~~~text
ShortcutRuntime failure
→ zero ShortcutLaunch retirement attempt
~~~

### Retirement failure

~~~text
external action already succeeded
retirement not proven
→ do not force publisher resume
→ return bounded failure
→ leave existing fail-safe retirement behavior authoritative
~~~

### Screenshot regression

Keep proving:

~~~text
ShortcutScreenshot retirement occurs BEFORE capture
~~~

Do not replace Screenshot with the new post-launch flow.

---

## 22. Overlay transport regression

Existing Overlay contract must remain:

~~~text
FrontendShortcutTile
→ no Action payload
→ no CloseOverlayAfterLaunch field

OverlayShortcutExecuteRequest
→ TileId only
~~~

Keep existing tests proving TileId-only execution.

Do not bump:

~~~text
OverlayTransportProtocol.CurrentVersion
~~~

for this feature.

---

## 23. Manual validation

### Steam Big Picture default

Create a new:

~~~text
Steam Big Picture
~~~

Shortcut.

Expected editor default:

~~~text
Close Overlay after launch [ON]
~~~

Open Overlay → Shortcut → execute it.

Expected:

~~~text
BPM launch requested successfully
→ Overlay closes
→ Full1902 capture retirement completes
→ input resumes on the Runtime-selected presentation
~~~

### Steam default

Same:

~~~text
Steam
Close Overlay after launch [ON]
~~~

Successful launch closes Overlay.

### Xbox default

Same:

~~~text
Xbox
Close Overlay after launch [ON]
~~~

Successful activation closes Overlay.

### Override OFF

Edit each built-in and turn:

~~~text
Close Overlay after launch [OFF]
~~~

Then execute from Overlay.

Expected:

~~~text
destination launch succeeds
→ Overlay remains open
~~~

### Generic action opt-in

Create an Application or Website Shortcut.

Default:

~~~text
Close Overlay after launch [OFF]
~~~

Turn it ON and verify successful execution retires Overlay.

### Failure

Configure a launch action that fails.

Even when close is ON:

~~~text
launch failure
→ Overlay remains visible
~~~

### Screenshot

Verify:

~~~text
Screenshot
→ no editable close-after-launch toggle
→ Overlay hides before capture
→ screenshot contains no Addon Overlay
~~~

---

## 24. Required automated validation

Run:

~~~powershell
dotnet build .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test .\tests\SteamInputAddonforClaw.UiTests\SteamInputAddonforClaw.UiTests.csproj -c Release --no-restore
git diff --check
~~~

---

## 25. Overengineering guardrails

Do not add:

- backward-compatible nullable persisted preference;
- migration framework;
- legacy schema reader;
- action-derived Runtime fallback for missing data;
- Overlay-side action inspection;
- Overlay-side close-policy logic;
- new Overlay protocol fields;
- generic launcher manager;
- generic dismissal manager;
- new visible-surface lock;
- new controller lifecycle state;
- new presentation switch code;
- launch rollback.

The intended ownership is:

~~~text
ShortcutTileDefinition
→ one explicit CloseOverlayAfterLaunch bool

Main App editor
→ chooses/stores it
→ creation default ON only for Steam/BPM/Xbox

Overlay
→ still sends TileId only

ShortcutRuntime
→ executes exact current tile
→ returns one internal post-success retirement directive

AddonProcessHost
→ reuses the existing OQ4 retirement path
~~~

---

## 26. Completion condition

Complete only when:

~~~text
new Steam Big Picture Shortcut
→ close default ON

new Steam Shortcut
→ close default ON

new Xbox Shortcut
→ close default ON

new EXE / PowerShell / Website Shortcut
→ close default OFF

all non-Screenshot launch Shortcuts
→ user can change the option
~~~

and:

~~~text
successful launch + option ON
→ existing unified Overlay retirement
→ same Full1902 presentation resumes safely

successful launch + option OFF
→ Overlay stays open

failed launch
→ Overlay stays open

Screenshot
→ existing mandatory pre-capture retirement unchanged
~~~

while:

~~~text
Shortcut root schema = 2
no migration / no legacy compatibility
action schema remains 1
Overlay execution remains TileId-only
Overlay transport unchanged
Full1902 controller ownership unchanged
no new manager/authority/state machine
~~~
