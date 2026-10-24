# Work Order — B2: Y Button Quick Jump (Main App + In-Game Overlay)

**Date:** 2026-10-24  
**Repository:** `onehoon/SteamAddonforClaw`  
**Status:** Ready for local Codex implementation  
**Reviewed baseline:** `main@d073cb4fca63c25f71ae8ed57c7d007593153e92` (PR #873 Squash Merged)  
**Scope:** One end-to-end B2 implementation PR, after the completed B1 tab-order UI work  
**Product:** Standalone Full PID1902 Windows app; one user / one interactive session; no CTW integration  
**Execution:** Local Codex implements code and automated tests; physical handheld and in-game visual validation are performed by the user **after code-correct merge** and are never PR/CI blockers.

## 0. User decisions — freeze the behavior

Add one shared, persistent **Y Button Quick Jump** preference. While the Addon Quick Overlay is open and its existing physical input capture is active, pressing **Y** navigates immediately to the selected Overlay tab.

- **Allowed options:** `Profile`, `Shortcut`, `Device`, `Controller`, `Setting`, and `Off`. All five targets are the existing fixed `AddonQuickSettingsTabId` identities, **not** tab indices.
- **Default:** `Profile`, including fresh installs and existing settings files that do not yet contain this preference.
- **Off:** Y causes **no Overlay navigation**. It is not a toggle for routing, button emulation, or game input.
- **Already viewing target:** no-op; do not reset row focus or scroll unnecessarily.
- **When the target is another tab:** select it through the same existing Overlay shell navigation/selection visual path used for tab changes.
- **Independent of active game:** Steam game, XBOX game, or no game uses the same selected destination. Jumping to Profile must **not** manufacture a game target or change the existing Profile data capture/recheck logic.
- **Independent of tab order:** reordering tabs never changes the Y target, and Y never changes the saved tab order.
- **PR #872 unchanged:** **each new Overlay Show** still starts at the current authoritative tab order's first element (order[0]), not at the configured Y target.
- **Current controls unchanged:** LB/RB cycle tabs; DPad/sticks navigate rows; A activates, B goes back/closes. X, LT, RT remain reserved/unmapped. No additional user-customizable bindings.

### Main App — exact card position

In `Quick Overlay`, insert **Y Button Quick Jump** as the **FIRST** top-level option card with a six-choice native ComboBox.

```text
Quick Overlay

[Y Button Quick Jump                        [Profile v]]  ← NEW FIRST CARD
[Show only current power source                  [toggle]]  ← existing
[Quick Overlay Tab Order                            [v]]  ← existing SettingsExpander; initially collapsed
   (five Up/Down editor rows only when expanded)
```

A visible descriptive subtitle such as "Press Y to jump to the selected tab while Quick Overlay is open." is appropriate.

The existing `Quick Overlay Tab Order` expander remains a **SettingsExpander**, default `IsExpanded="False"`, and the existing Power Source card stays in its relative position. Do not move them to another Main App page.

### In-game Overlay — setting placement

In the in-game `Setting` tab, inside the **existing** expandable `Quick Settings` card, add one compact ordered-choice row **"Y Button Quick Jump"** above the existing `"Show only current power source"` toggle.

```text
Setting
  ClawHUD                     [expand]
  Quick Settings              [expand]
     Y Button Quick Jump      [<] Profile [>]
     Show only current power source   [toggle]
  Quick Overlay Tab Order     [expand]
     (unchanged five-row editor)
```

The in-game choice uses the existing controller-first `OverlayValueRow` / `OverlayValueButtonKind.DiscreteChoice`; Left/Right and touch/pointer buttons change the selection one option at a time. Do not invent a ComboBox overlay renderer or add another Setting card solely for Y.

## 1. Required source review and precedence

Use latest `main` at implementation time, not historical snippets alone.

Read the authoritative project documents:
- `docs/Full 1902 Implementation/README.md` and its cited current ownership/lifecycle precedence, especially `FULL_1902_IMPLEMENTATION_ARCHITECTURE.md` and `FULL1902_ELEVATED_RUNTIME_ARCHITECTURE_2026-10-05.md`.
- `docs/overlayui/README.md`, `ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md`, and `ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md`. Treat older Steam-QAM/CTW proposals as retired.
- `docs/work-order/2026-10-24_OVERLAY_SHOW_FIRST_CONFIGURED_TAB_PR_A_WORK_ORDER.md` / merged PR #872.
- `docs/work-order/2026-10-24_QUICK_OVERLAY_TAB_ORDER_EDITOR_AND_MOVE_ICONS_B1_WORK_ORDER.md` / merged PR #873.
- `docs/work-order/OVERLAY_SHARED_PRODUCTION_CONTROLS_PARITY_WORK_ORDER_2026-10-04.md` for existing settings snapshot and mutation transport conventions.

Inspect these actual implementation points before editing:
- `src/SteamInputAddonforClaw/Settings/AppSettings.cs`, `SettingsStore.cs`, `StartupSettingsCoordinator.cs`.
- `src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs` and `AddonQuickSettingsShellContracts.cs`.
- `src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs`, especially `MapSettings()` and the existing Power Source setter.
- `src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs`, `NamedPipeAddonFrontendClient.cs` and `NamedPipeAddonFrontendServer.cs`.
- `src/SteamInputAddonforClaw.UI/Views/QuickOverlayPage.xaml` and `QuickOverlayPage.xaml.cs`; both resw locales.
- `src/SteamInputAddonforClaw/Lifecycle/OverlayControllerInputRouter.cs`; `src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs`.
- `src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs`, `OverlayWire.Validation.cs`, `NamedPipeOverlayClient.cs`, `NamedPipeOverlayServer.cs`, `src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs`.
- `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`, `OverlayWindow.Shell.cs`, `OverlayWindow.Navigation.cs`, `OverlayWindow.ClawHud.cs`, `OverlayWindow.Controller.cs` and `OverlayValueRow.cs`.

### Confirmed technical baseline

- Current Main App Frontend pipe contract version **88**; Current Overlay pipe contract version **21**. B2 alters both payload/command contracts: **bump each exactly once**, re-reading the latest version before implementing; do not assume fixed numbers if intervening commits land.
- `AppSettings` is persisted through `SettingsStore`; existing settings load is a **manual per-field JSON projection**, so simply adding an init property is **not enough** — wire the reader too. Save already serializes the settings record with enums by string names.
- `FrontendSettingsSnapshot` flows through both Main App bootstrap and in-game `FrontendSettingsState`/existing settings mutation response.
- Main App and Runtime are separate processes. A new `IAddonFrontendControl` setting setter needs its regular `FrontendRpcMethod` + request DTO + named-pipe client/server dispatch. It is **not** enough to add an in-process setter.
- The Overlay controller router currently maps DPad/A/B/LB/RB and sticks, but **Y is not mapped**. It compares rising edges at `IMsiClawPreparedInputSource.StateChanged` and consumes mapped controls during the existing release gate. No raw controller reports cross the Overlay IPC.
- Overlay semantic navigation is `OverlayNavigationAction` via `OverlayProcessController.SendNavigationAsync` → `NamedPipeOverlayServer`/`Client` → `App.HandleNavigationAsync` → `OverlayWindow`. Reuse it.
- The in-game `Setting → Quick Settings` card currently has exactly one Power Source toggle and `_quickSettingsRows` selection wrapping. `OverlayValueRow` already supports formatted ordered choices, controller Left/Right adjustments, native arrow buttons, and authoritative repaint.
- `OverlayWindow.ApplyFrontendSettingsState` is currently implemented in the Controller partial, and it also updates the Setting page's Power Source row. The existing `ApplyFrontendSettingsMutationResult` and `ApplyFrontendSettingsMutationFailure` treat the "neither LED nor Controller-for-Steam" category as **Power Source**. B2 must distinguish Y mutations so the Power Source pending state isn't mistakenly cleared.

## 2. Minimal shared preference contract

A small dedicated **preference enum** is appropriate because this control has a real **Off** option, whereas `AddonQuickSettingsTabId` represents exactly five actual tabs. Never add `Off` to `AddonQuickSettingsTabId` — that would violate the fixed five-tab order contract.

Preferred type, in existing shared Frontend contracts (or adjacent small contract file):

```csharp
public enum QuickOverlayYButtonTarget
{
    Profile = 0,
    Shortcut = 1,
    Device = 2,
    Controller = 3,
    Setting = 4,
    Off = 5,
}
```

The explicit numeric order matches the UI's selectable sequence. Default is `Profile` rather than enum zero accidentally meaning Off. Map non-Off targets to the corresponding **existing** `AddonQuickSettingsTabId` by one small, clear switch near the consumer; do not use the enum's integer as a tab-array index.

Extend:
- `AppSettings` with `QuickOverlayYButtonTarget YButtonQuickJumpTarget { get; init; } = QuickOverlayYButtonTarget.Profile;`.
- `StartupSettingsCoordinator` with a read-only property and one narrow `ChangeYButtonQuickJumpTarget(QuickOverlayYButtonTarget target)` method using the existing `SettingsStore.Save(next) → Settings = next` pattern.
- `SettingsStore.Load` with a **field-isolated reader**: missing → Profile; valid enum name → value; malformed/unknown/numeric value → Profile with a feature-local log and no loss of unrelated settings. Do not make one invalid Y value invalidate the whole app settings JSON.
- `FrontendSettingsSnapshot` with a typed init property, same default Profile.
- `InProcessAddonFrontendControl.MapSettings()` with the authoritative saved Y choice.
- `IAddonFrontendControl.SetQuickOverlayYButtonTargetAsync(target, token)` and a concrete in-process method: Runtime validates `Enum.IsDefined(target)`, persists only valid changed values, returns actual `FrontendSettingsSnapshot`, and triggers existing `StateInvalidated` on change. Invalid values must not be accepted or published as success; using a standard argument exception at this method's trusted boundary is fine.

Keep a **single persisted setting**. Do not add independent Main UI / Overlay stores or tab-order linkage. No migration layer is required: missing property naturally receives the Profile default. Keep enum serialization consistent with `SettingsStore`'s existing string-enum convention.

## 3. Main App frontend RPC (required)

Extend the **existing** frontend pipe:
- `FrontendRpcMethod.SetQuickOverlayYButtonTarget`.
- A single typed DTO such as `SetQuickOverlayYButtonTargetRequest(QuickOverlayYButtonTarget Target)`.
- `NamedPipeAddonFrontendClient.SetQuickOverlayYButtonTargetAsync` invokes the new method with that DTO.
- `NamedPipeAddonFrontendServer` dispatches to the existing `_inner.SetQuickOverlayYButtonTargetAsync` and returns `FrontendSettingsSnapshot`.
- Update version comment and `FrontendTransportProtocol.CurrentVersion` **once** (88→89 if still current).
- Validate incoming enum on the Runtime setter. The existing request/response pattern supplies request correlation; do **not** introduce another permission layer, second pipe, or custom transport client.

Main UI no extra polling, and no MainWindow navigation modifications.

## 4. Main App Quick Overlay first card

Modify `QuickOverlayPage.xaml`, preserving the existing Power Source and default-collapsed Quick Overlay Tab Order controls.

Add **first**:

```xml
<ctcontrols:SettingsCard
    x:Name="YButtonQuickJumpCard"
    x:Uid="QuickOverlay_YButtonQuickJumpCard"
    Header="Y Button Quick Jump"
    Description="Press Y to jump to the selected tab while Quick Overlay is open.">
    <ctcontrols:SettingsCard.HeaderIcon>
        <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE7FC;" />
    </ctcontrols:SettingsCard.HeaderIcon>
    <ComboBox x:Name="YButtonQuickJumpComboBox"
              SelectionChanged="YButtonQuickJumpComboBox_SelectionChanged">
        <!-- six choices, Profile / Shortcut / Device / Controller / Setting / Off -->
    </ComboBox>
</ctcontrols:SettingsCard>
```

E7FC is already used as a native controller category icon in the Main App; use it rather than inventing artwork.

- Provide six fixed ComboBox options in exactly the shared enum order (`Profile, Shortcut, Device, Controller, Setting, Off`), with stable Tag/value identifiers independent of localized visible text.
- Localize title/description, visible option texts (at least `Off`/the five tab names where needed), and accessibility through `en-US` and `ko-KR` `Resources.resw`. Use existing `x:Uid` and `MainUiStrings` style.
- On Initialize, select the value from the already supplied `bootstrap.Settings.YButtonQuickJumpTarget` without sending a mutation. Keep a suppression flag for programmatic `SelectionChanged`.
- On activation/visible invalidation, read the saved option through the **existing** `GetBootstrapAsync` refresh path already used by this page; do not add a second redundant bootstrap call just for Y. The separate tab-order capture remains independent.
- On user selection, invoke `_frontend.SetQuickOverlayYButtonTargetAsync`. While pending, disable this ComboBox to prevent duplicate edits; accept/render **returned authoritative** target, not only local optimistic selection.
- On failure, log and roll back to authoritative bootstrap readback or the last confirmed target if that read fails. Do not show a global failure banner, relaunch, or mutate another setting.
- Protect against an already-started refresh overwriting the just-completed Y mutation (reuse the page's established pending/refresh guard pattern; do not add global epoch/version/queue infrastructure).
- Keep Power Source and tab-order mutation guards independent.

**Layout order must remain: Y Button Quick Jump first, Show only current power source second, Quick Overlay Tab Order expander third (collapsed by default).**

## 5. Runtime Y button input route and capture safety

In `OverlayControllerInputRouter.Bindings` append:

```csharp
(b => b.Y, OverlayNavigationAction.QuickJump),
```

Add exactly **one semantic navigation action** `QuickJump` to `OverlayNavigationAction` and bump Overlay protocol version once (21→22 if still current). The raw physical Y button never travels across the wire.

Important lifecycle semantics:
- Rising-edge only; holding Y must not repeat navigation.
- If Y is already held when capture starts, no jump until it has been released and pressed again (the existing `_previousState` initialization already handles this).
- Once Y is mapped as a consumed Overlay control, the existing release gate **must also wait for its release** when the Overlay is closed. This applies even when target = Off, because the input binding/release accounting remains simple and static; Off disables navigation, not release safety.
- Release, physical source loss, stop accepting, dispose, restart/suspend paths continue through existing routing; no second input reader, hook, timer, or new gate.
- A quick jump is handled only while the existing Overlay capture/visibility path admits navigation. It must **never** route Y while the Overlay is hidden or restore game-facing virtual output from inside the navigation handler.
- Do not change X, LT, RT, M1/M2, OEM button mappings, steam/XBOX output, Full1902 routing, HidHide, VIIPER ownership or teardown.

The actual destination must be resolved in the Overlay window from the current **authoritative** Y preference (below), not hard-coded to Profile in the router. Off is therefore a no-op.

## 6. Overlay visual selection for QuickJump

Implement a small `OverlayWindow.QuickJumpToConfiguredTab()` method near `OverlayWindow.Shell.cs` or another existing Overlay partial:

```csharp
internal void QuickJumpToConfiguredTab()
{
    if (!_frontendSettingsAvailable) return;

    var target = _frontendSettingsSnapshot!.YButtonQuickJumpTarget;
    var tabId = target switch
    {
        QuickOverlayYButtonTarget.Profile => AddonQuickSettingsTabId.Profile,
        QuickOverlayYButtonTarget.Shortcut => AddonQuickSettingsTabId.Shortcut,
        QuickOverlayYButtonTarget.Device => AddonQuickSettingsTabId.Device,
        QuickOverlayYButtonTarget.Controller => AddonQuickSettingsTabId.Controller,
        QuickOverlayYButtonTarget.Setting => AddonQuickSettingsTabId.Setting,
        QuickOverlayYButtonTarget.Off => (AddonQuickSettingsTabId?)null,
        _ => null, // fail closed
    };
    if (tabId is not { } selected || selected == _tabState.SelectedTab)
        return;

    _tabState.Select(selected);
    ApplySelectedTabVisualState();
}
```

Adjust naming/field access to fit current code, but **reuse** existing selected-tab path. No new navigation owner or forced show. The target tab must be selected by identity even if the user has reordered all five tabs.

In `src/SteamInputAddonforClaw.Overlay/App.xaml.cs`, `HandleNavigationAsync` adds one case:

```csharp
case OverlayNavigationAction.QuickJump:
    _window?.QuickJumpToConfiguredTab();
    break;
```

Keep the existing `_userDismissInProgress` guard. Do not add a competing Y handler in Runtime Host, main UI, or raw overlay window input. No synthetic keys or Steam QAM side effects.

## 7. Overlay Setting → Quick Settings editor and settings mutation

### 7.1 Render an existing discrete-choice row

In `OverlayWindow.ClawHud.cs` `BuildQuickSettingsPage()`, place a new `OverlayValueRow` with label **Y Button Quick Jump** before the existing Power Source toggle:

```csharp
_yButtonQuickJumpRow = new OverlayValueRow(
    "Y Button Quick Jump",
    FormatYButtonQuickJumpChoice,
    value => RequestYButtonQuickJumpChange((QuickOverlayYButtonTarget)(int)Math.Round(value)),
    OverlayValueButtonKind.DiscreteChoice);
```

Use the existing `CreateSettingDetailRow(..., SettingCardId.QuickSettings)` and `RegisterRowPointerSelection(...)` so it participates in the same gamepad row navigation, collapse behavior, focus management and pointer selection as the Power Source row.

- Discrete order: `Profile, Shortcut, Device, Controller, Setting, Off`.
- Formatter displays the **corresponding readable choice**, not a numeric value; Off must be visible as "Off".
- ApplyState with valid fixed range 0…5, step 1, authoritative (or narrowly pending) value.
- Disable changes when settings are unavailable or the **Y preference mutation** is pending.
- Use the current settings snapshot `YButtonQuickJumpTarget`; no separate UI-only default when Runtime state has not arrived.
- Local Left/Right changes must go through existing row model and one typed mutation, not directly change persisted values.
- Preserve row selection identity and keep the existing `Quick Overlay Tab Order` card and its position unchanged.

### 7.2 Extend the existing correlated production-settings IPC

The current Overlay request shape has nullable fields for the four existing alternatives; B2 adds an **optional enum field** (nullable for "no Y mutation") in the same record, e.g.:

```csharp
internal sealed record OverlayFrontendSettingsMutationRequest(
    long RequestId,
    ControllerLedSettings? ControllerLed = null,
    bool? CurrentPowerSourceOnly = null,
    SteamVirtualControllerPreference? SteamVirtualController = null,
    bool? GyroscopeEnabled = null,
    QuickOverlayYButtonTarget? YButtonQuickJumpTarget = null);
```

Unlike a nullable TabId, the enum's concrete `Off` value means a real request even when optional outer field is nullable.

Add distinct message kinds `YButtonQuickJumpMutationRequest` and `YButtonQuickJumpMutationResult` to the current `OverlayWireMessageKind`. **Reuse** the existing `OverlayFrontendSettingsMutationRequest/Response` envelope, correlation sequence/gate, `NamedPipeOverlayClient.SendFrontendSettingsMutationAsync`, `NamedPipeOverlayServer.HandleFrontendSettingsMutationRequestAsync`, and `AddonProcessHost.MutateOverlayFrontendSettingsAsync`. Do **not** make a second Overlay settings pipe, a second request completion queue, or an entirely separate result DTO.

**Important correlation detail:** the client already tracks `_pendingProductionSettingsIsLed` and `_pendingProductionSettingsIsControllerForSteam`; the newly added Y request/result must not be confused with an in-flight Power Source result. Choose one minimal explicit discriminant for the shared frontend-settings send helper (e.g. an existing-kind enum or a small Y bool), and ensure the client checks expected response kind for the same request ID. A full generic mutation framework is **not** required.

Update in the existing validation/server/client files:
- `OverlayProductionControlsWireValidation`: validate Y request contains exactly one defined Y enum target and no LED, Power Source, Steam Controller or Gyroscope mutation fields. Validate Y result's existing response record and returned Y enum state. Update structural FrontendSettingsState and existing mutation result validation to reject undefined Y values where relevant; keep null/other payload checks.
- `NamedPipeOverlayServer`: include Y request in the existing allowlist and admitted-while-visible handler; choose the paired Y result kind. Never allow it after visibility/capture admission is gone.
- `NamedPipeOverlayClient`: add `SendYButtonQuickJumpMutationAsync(target)` using the existing settings send gate and correlation. Accept only matching Y response kind and valid target, without weakening LED/Power Source/Controller-for-Steam validation.
- `AddonProcessHost.MutateOverlayFrontendSettingsAsync`: dispatch Y to `_frontendControl.SetQuickOverlayYButtonTargetAsync` only on existing Overlay-capture admission; return current settings readback on success/rejection, same as other settings mutations.
- `OverlayWire`: bump protocol version once with a version-history note. Both Runtime and Overlay are produced by the same installation; no backward shim.
- `OverlayWindow` and `Overlay.App`: emit the Y mutation through the existing request pipeline and handle resulting authoritative settings state.

**Important in existing OverlayWindow mutation handling:** today the generic `ApplyFrontendSettingsMutationResult/Failure` assumes any mutation that is not LED or Controller-for-Steam is Power Source. Extend that small dispatch in a feature-specific way so:
- Y success/failure clears only Y pending state and re-renders only relevant setting choice state.
- It **does not** inadvertently call `SetQuickSettingsCurrentPowerSourceMutationInFlight(false)`.
- Power Source success/failure still clears only its own pending state.
- LED/Controller-for-Steam mutation flows remain unchanged.
- On failed Y write, show the last authoritative Y value from the returned response, or retain last confirmed value when transport has failed; no false confirmation.
- Current `FrontendSettingsStateReceived` updates both Power Source and Y choices; a Main App change is eventually reflected in-game through existing `StateInvalidated → RefreshFrontendSettingsAsync`.

An ordinary Y **navigation** press does **not** send a settings mutation; only editing the Y option does.

## 8. Mandatory practical safety and failure policy

Protect realistic behavior without expanding architecture:
- **Startup / missing settings key:** Profile default, still opens at order[0].
- **Existing invalid Y preference string:** ignore just that field, default Profile, preserve all unrelated settings.
- **Unrecognized target on incoming Overlay wire:** reject malformed state/mutation; do not index arbitrary tab enum or select an invalid tab.
- **Overlay not ready / settings state not yet received:** ignore QuickJump until a valid authoritative state is available. No crash or fake Profile fallback.
- **Y pressed while setting target is Off:** no tab change; release gate still covers held Y.
- **Y held through dismiss:** no jump repeat and do not resume game-facing publication until consumed Y is released (or existing source-unavailable/teardown handling takes over).
- **Physical source lost mid-Overlay:** existing router's source-loss/shutdown behavior remains the one cleanup authority. No additional device reads.
- **Settings mutation rejected / IPC unavailable:** preserve current setting, return/retain authoritative UI value and log; do not kill Overlay, force routing, or alter other settings.
- **Main App and Overlay choose different values near each other:** persisted Runtime state is final authority; do not implement extra epochs, locks, election, conflict managers or speculative timing gates for unsupported scenarios.
- **Overlay closed/reopened:** starts on configured order[0] each time (PR #872); Y target remains persisted separately.
- **Sleep/hibernate/restart/crash/shutdown:** no changes to lifecycle gates, owner, fail-close or teardown paths.

## 9. Tests to update / add

Keep existing tests; adapt only directly obsolete assertions. Focus on realistically reachable correctness.

### Settings/persistence and Main App RPC
- `tests/SteamInputAddonforClaw.Tests/SettingsStoreTests.cs`: default Profile on fresh/missing, valid choices including Off survive save/reload as strings; malformed/unknown/numeric Y value falls back locally and preserves other settings; invalid setter input rejected.
- In-process frontend tests for one persisted preference, no-op repeated writes, `MapSettings` projection, `StateInvalidated` on real change.
- Frontend pipe client/server request/response round trip, invalid target rejection, version test updated from 88 to next actual value.
- `tests/SteamInputAddonforClaw.UiTests/QuickOverlayPageTests.cs`: first option card and exact six choices; startup selection uses snapshot without sending; change through typed setter with readback/failure restore and guard; `RequestRefresh` still refreshes Power Source and tab order correctly.
- `tests/SteamInputAddonforClaw.UiTests/MainUiLocalizationResourceTests.cs`: update hard-coded Quick Overlay `x:Uid` count/key set; confirm en-US/ko-KR option labels and ordering of first card, Power Source, expander.
- `tests/SteamInputAddonforClaw.UiTests/OptionCardIconContractTests.cs`: existing 41 expected icon-bearing cards → **42**, including Y first card's E7FC; preserve every previous entry.

### Runtime input and Overlay navigation
- `tests/SteamInputAddonforClaw.Tests/OverlayControllerInputRouterTests.cs`: Y rising edge emits exactly one QuickJump; held Y never repeats; already-held Y on capture start is ignored until release/repress; source loss prevents further events; release gate waits for held Y even when the Y target preference is Off.
- Update the old "unmapped buttons" test which currently includes **Y as unmapped**: remove Y from its unmapped fixtures, keep X/LT/RT tests. Do not silently weaken it.
- `tests/SteamInputAddonforClaw.Tests/OverlayTransportTests.cs`: semantic QuickJump serialization/validation and version 21 → 22 (if still latest), including any **hard-coded serialized version JSON fixtures**; `Enum.GetValues` navigation tests naturally include the new member.
- Overlay UI tests (e.g. `OverlayDeviceRendererWiringTests.cs`, `OverlayTabStateTests.cs`, `OverlayTabOrderEditorTests.cs`): configured target by ID, custom tab order, already-current no-op, Off no-op, unavailable settings no-op; verify `ResetForShow` remains independent and equal to order[0].
- In-game Setting tests: new ordered choice appears **inside** Quick Settings above Power Source; it is selectable only when expanded, its controller Left/Right + native buttons use `OverlayValueRow` and emit typed setting edits; pending/failure restores authoritative value without clearing Power Source pending state.

### Overlay correlated settings transport
- Follow `tests/SteamInputAddonforClaw.Tests/OverlayControllerForSteamTransportTests.cs` and related production controls transport tests for a focused new Y request/result round-trip on the **existing** server/client.
- Ensure undefined/mixed mutation payloads are rejected; identical RequestId with wrong result kind is rejected; a normal rejected mutation returns authoritative settings; disallowed/not visible requests cannot mutate state.
- Ensure Power Source, LED, Gyroscope and Controller-for-Steam existing tests continue passing. Avoid introducing a new mock transport abstraction to test this one option.

### Suggested local software validation

```powershell
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Debug
dotnet test tests/SteamInputAddonforClaw.UiTests/SteamInputAddonforClaw.UiTests.csproj -c Release
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Release
dotnet build SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Run normal repository CI. **Do not** require physical controller access, on-device validation logs, actual game sessions, or screenshots as Codex acceptance, review conditions, or merge blockers. The user verifies hardware behavior after merge.

## 10. UX text, docs and scope boundaries

Add `en-US` and `ko-KR` resources for:
- `QuickOverlay_YButtonQuickJumpCard.Header` = "Y Button Quick Jump" / "Y 버튼 빠른 탭 이동"
- `QuickOverlay_YButtonQuickJumpCard.Description` = "Press Y to jump to the selected tab while Quick Overlay is open." / "퀵 오버레이를 연 상태에서 Y 버튼을 누르면 선택한 탭으로 바로 이동합니다."
- Six ComboBox option labels (use existing tab labels only if their current localized resource is appropriate).
- Do not rename `Quick Overlay Tab Order` or change its saved order/product.

Update `README.md` and `docs/howtouse/README_KO.md` Quick Overlay guides to describe Y and default Profile, Off, and the separation between initial tab order[0] and Y destination. Existing B1 docs are authoritative for tab-order UI and should remain accurate.

**Expected production files:** existing settings and settings store/coordinator; shared Frontend contract; InProcess frontend; FrontendWire/RPC Client/Server; Main App QuickOverlayPage XAML/code-behind + both locales; router; OverlayWire/validation + Overlay NamedPipe client/server; Overlay Host mutation dispatcher; Overlay App + OverlayWindow Setting/Shell/Controller partials; focused tests and help docs.

**Out of scope:**
- Refactoring Quick Overlay page into a generic settings framework.
- Custom remappable controls beyond Y, or Y double-tap/long-hold/repeat/debounce settings.
- Editing/defaulting/reordering the five tabs (PR #873 already owns tab order).
- "Open directly on Profile whenever game active" behavior (removed by PR #872).
- Main App page navigation changes, new Overlay tabs/cards, main/overlay IPC redesign.
- Additional input owners, hooks, polling, retry workers, lock graphs or speculative race barriers.
- CTW integration, Steam QAM patching, SteamDeck virtual output semantics.
- Changes to Full1902 physical PID1901/PID1902, PnP, HidHide, VIIPER, rollback/fail-close or shutdown teardown.

## 11. Acceptance checklist

- [ ] Saved Y choice is one valid enum among Profile, Shortcut, Device, Controller, Setting, Off; defaults Profile.
- [ ] Main App Quick Overlay first card is Y Button Quick Jump; Power Source second; default-collapsed Quick Overlay Tab Order third.
- [ ] Main App six-choice ComboBox is functional via Runtime frontend RPC and reflected in authoritative settings; no optimistic-only persistence.
- [ ] In-game Setting → Quick Settings offers a Y ordered-choice row **before** Power Source, using existing Left/Right and pointer control semantics.
- [ ] Both UIs share one Runtime-persisted Y target; each reflects changes made in the other on normal refresh.
- [ ] Y rising edge while Overlay capture is active jumps directly to the requested existing tab identity, or does nothing for Off/current target.
- [ ] Y held on Overlay open does not fire until repressed; held Y is included in consumed-control release wait.
- [ ] X/LT/RT and existing LB/RB/A/B/DPad/sticks navigation/capture remain unchanged.
- [ ] Next Overlay Show always selects saved `TabOrder[0]`, irrespective of Y target or game activity.
- [ ] Existing Profile data / no-game state, card expansion, controller/power mutations and tab-order moves remain unaffected.
- [ ] Both Frontend and Overlay pipes version/validation/test contracts are updated, with no protocol fallback shims.
- [ ] Automated tests, localization checks, Release build and CI pass; physical MSI Claw test remains exclusively user-owned post-merge.

**Handoff:** Implement this as one focused B2 PR in local Codex, with multiple commits only if convenient (e.g. contract/persistence+Frontend RPC, Overlay navigation+wire, UI+tests). Do not split into multiple deployment phases because Y must not appear as an inert option card.
