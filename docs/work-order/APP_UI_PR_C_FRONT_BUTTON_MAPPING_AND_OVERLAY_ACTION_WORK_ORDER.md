# Work Order — App UI PR-C: Full1902 Front-Button Mapping + Quick Settings Overlay Action

> **Date:** 2026-09-04  
> **Status:** Ready for implementation  
> **Reviewed baseline:** `main` at `e255b44b4ed4d0bfcb68a3cfde71dee07f424f0b`  
> **Primary UI authority:** `docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md`  
> **Controller authority:** `docs/Full 1902 Implementation/README.md` and the current Full1902 authority documents listed below

---

## 1. Goal

Finish the user-facing front-button mapping model for the current Full1902 product.

The MSI Claw has two relevant front buttons that the Addon already observes in software:

```text
internal WING / Event88  → user-facing name: Gamebar Button
internal OEM1 / Event41  → user-facing name: Center M Button
```

The Controller page must expose both buttons, in this order:

```text
Controller

Button Mapping

Gamebar Button
Center M Button
```

Each physical button has exactly one action for each of two runtime presentation domains:

```text
Normal
Steam Game / Big Picture
```

The product defaults are frozen as:

```text
Normal:
  Gamebar Button  → Quick Settings Overlay
  Center M Button → Steam Big Picture

Steam Game / Big Picture:
  Gamebar Button  → Steam Button
  Center M Button → Steam Quick Access
```

`None` is not a valid physical-button action in either domain.

Within one domain, the two physical buttons may not use the same action.

This PR also adds `Quick Settings Overlay` as a real selectable front-button action and routes it through the existing Runtime-owned coordinated Overlay toggle path.

---

## 2. Read Before Implementation

Read these documents before editing code:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/work-order/OQ3_A_MAIN_UI_OVERLAY_VISIBLE_SURFACE_COEXISTENCE_WORK_ORDER.md
docs/work-order/OQ4_CONTROLLER_CAPTURE_NEUTRAL_PUBLICATION_WORK_ORDER.md
docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md
```

Where older WING/OEM1 work orders conflict with this work order, this work order is the current button-mapping product decision.

Do not change Full1902 controller authority while implementing this UI/action redesign.

---

## 3. Current Source State Verified Against `main`

### 3.1 Controller UI currently exposes only OEM1

Current:

```text
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
```

renders only:

```xml
<views:CenterMButtonPage x:Name="CenterMInlineContent" />
```

There is no current WinUI Gamebar/WING editor.

`CenterMButtonPage.xaml` currently exposes only one OEM1 setting:

```text
Center M Button
└ Normal Action
```

### 3.2 WING settings/runtime already exist

Current WING contract:

```text
WingMappingSettings
├ Single
└ Double
```

Current WING actions:

```text
None
SteamButton
KeyboardHotkey
LaunchApplication
```

Current default:

```text
Single = SteamButton
Double = None
```

The Runtime already has:

```text
Wing Event88 acquisition
WingGestureRecognizer
WingEventGestureBridge
WingActionDispatcher
persisted WingMappingSettings
SetWingMappingAsync frontend RPC
```

The missing part is not the physical Event88 path. The missing part is the final Full1902 mapping model and UI.

### 3.3 OEM1 has an older four-slot mapping model

Current OEM1 contract:

```text
Oem1MappingSettings
├ RemappingEnabled
├ NormalSingle
├ NormalDouble
├ RoutingSingle
└ RoutingDouble
```

Current defaults:

```text
RemappingEnabled = true
NormalSingle     = SteamBigPicture
NormalDouble     = None
RoutingSingle    = SteamQuickAccess
RoutingDouble    = None
```

The current Controller UI already forces `RemappingEnabled = true` and exposes only `NormalSingle`, so the persisted switch and hidden double slots no longer describe the intended product UI.

### 3.4 OEM1 already resolves its domain from actual presentation

Current `MsiClawFrontButtonRuntime.Create(...)` receives:

```csharp
Func<bool> isSteamDeckPresentationActive
```

and production binds it to:

```csharp
_presentationOwnership?.ActivePresentation == AddonPresentationKind.SteamDeck
```

This is the correct authority for the new two-domain mapping model.

### 3.5 Overlay already has the correct Runtime lifecycle path

Current `AddonProcessHost` owns:

```text
ToggleOverlayForPoc()
→ CoordinateOverlayToggleAsync()
→ Main UI / Overlay visible-surface ordering
→ Overlay Show/Hide
→ controller capture
→ current virtual presentation neutralization
→ release-to-resume handling
```

The new physical-button action must reuse this path.

Do not call `OverlayProcessController.ShowAsync()` or `ToggleForPocAsync()` directly from a button dispatcher.

### 3.6 Settings are currently split between two persisted authorities

`AppSettings` currently contains both:

```text
Oem1Mapping
WingMapping
```

`StartupSettingsCoordinator` independently exposes:

```text
ChangeOem1Mapping(...)
ChangeWingMapping(...)
```

That shape cannot cleanly enforce the new cross-button invariant atomically.

### 3.7 Desktop frontend protocol is currently v24

Current:

```text
FrontendTransportProtocol.CurrentVersion = 24
```

This PR changes the frontend settings contract and replaces the two mapping mutation RPCs, so one protocol bump is required.

---

## 4. Frozen Product Model

The final user model is:

```text
physical button
×
presentation domain
→ exactly one action
```

Specifically:

```text
Gamebar Button
├ Normal
└ Steam Game / Big Picture

Center M Button
├ Normal
└ Steam Game / Big Picture
```

There is no user-facing:

```text
WING
OEM1
Single
Double
None
Remapping Enabled
Steam Input Routing
```

in the final Controller mapping surface.

`WING`, `OEM1`, Event88, Event41, recognizer names, and other hardware/internal names may remain where they are legitimate implementation terminology.

Do not perform a broad low-level rename merely to change the UI label.

---

## 5. Domain Authority — Use Actual Presentation, Not Raw Steam Detection

User-facing labels are:

```text
Normal
Steam Game / Big Picture
```

Runtime authority is not a new `IsSteamMode` setting.

Resolve the active mapping domain from the actual current Full1902 presentation:

```text
ActivePresentation == SteamDeck
→ Steam Game / Big Picture mapping

otherwise
→ Normal mapping
```

This is intentionally outcome-based.

Example:

```text
Steam game detected
→ SteamDeck presentation transition fails
→ active presentation remains Xbox360
→ front buttons MUST still resolve the Normal mapping
```

Do not add:

```text
FrontButtonSteamMode
ButtonMappingModeManager
separate Steam/BPM observer
persisted domain flag
epoch/barrier solely for mapping selection
```

Reuse the existing `isSteamDeckPresentationActive` callback/fact.

Capture the domain once per physical button dispatch and resolve that press from that fact.

Do not add extra revalidation around theoretical instruction-level crossings.

---

## 6. Replace the Split OEM1/WING Persisted Model With One Atomic Front-Button Mapping

The two buttons now share:

- the same two domains;
- the same domain-specific action vocabulary;
- one cross-button uniqueness invariant;
- one settings page/surface;
- one save operation.

Therefore use one persisted source of truth rather than keeping two separately writable mapping records.

Recommended contract location:

```text
src/SteamInputAddonforClaw.Contracts/FrontButtons/FrontButtonMapping.cs
```

Recommended conceptual shape:

```csharp
public enum FrontButtonKind
{
    Gamebar,
    CenterM
}

public enum FrontButtonDomain
{
    Normal,
    Steam
}

public enum FrontButtonAction
{
    QuickSettingsOverlay,
    SteamBigPicture,
    SteamButton,
    SteamQuickAccess,
    KeyboardHotkey,
    LaunchApplication
}

public sealed record FrontButtonBinding(...);

public sealed record FrontButtonDomainMapping(
    FrontButtonBinding GamebarButton,
    FrontButtonBinding CenterMButton);

public sealed record FrontButtonMappingSettings(
    FrontButtonDomainMapping Normal,
    FrontButtonDomainMapping Steam);
```

Exact record syntax may differ, but preserve these semantics:

```text
one persisted mapping object
four required physical-button/domain bindings
no None action
no optional button assignment
```

Use explicit factory/default construction so a zero-valued enum is not silently treated as a valid persisted mapping merely because a record was default-constructed.

Do not add a generalized input-binding framework.

---

## 7. Shared Action Configuration

The current WING and OEM1 contracts duplicate hotkey/application-binding structures.

Because this PR makes the two physical buttons share one action vocabulary, move the user-configurable binding data to the same front-button contract namespace.

Conceptually:

```text
FrontButtonHotkeyBinding
FrontButtonLaunchApplicationBinding
FrontButtonBinding
```

Preserve the existing supported behavior:

```text
Keyboard / Hotkey
→ modifiers + one key

Launch Application
→ .exe path + optional arguments
```

Do not expand this into:

```text
macro sequences
hold actions
scripts
shell files
multiple executables
process lifecycle management
```

The existing `.exe`-only picker / launcher safety policy remains.

The existing WING prohibition against mapping the Gamebar Button back to native `Win+G` must remain enforced.

Do not weaken Full1902 Win+G suppression by making `Keyboard / Hotkey = Win+G` an indirect Gamebar Button escape hatch.

---

## 8. Domain-Specific Action Capabilities

Both physical buttons use the same action list within a given domain.

### 8.1 Normal domain

Offer exactly:

```text
Quick Settings Overlay
Steam Big Picture
Keyboard / Hotkey
Launch Application
```

Do not offer:

```text
Steam Button
Steam Quick Access
```

because the SteamDeck presentation is not active in this domain.

### 8.2 Steam Game / Big Picture domain

Offer exactly:

```text
Quick Settings Overlay
Steam Button
Steam Quick Access
Keyboard / Hotkey
Launch Application
```

Do not offer:

```text
Steam Big Picture
```

because the Steam domain already represents an active Steam game/BPM-driven SteamDeck presentation.

### 8.3 One capability table

Define this once in Contracts so UI and Runtime validation share it.

Conceptually:

```csharp
FrontButtonActionCapabilities.ActionsFor(FrontButtonDomain domain)
FrontButtonActionCapabilities.Supports(FrontButtonAction action, FrontButtonDomain domain)
```

Do not create separate Gamebar and Center M capability tables for identical domain rules.

---

## 9. Frozen Defaults

The exact first-install/default mapping is:

```text
Normal
  Gamebar Button  = Quick Settings Overlay
  Center M Button = Steam Big Picture

Steam
  Gamebar Button  = Steam Button
  Center M Button = Steam Quick Access
```

Tests must lock these defaults.

Do not inherit the old WING default (`SteamButton` in every state).

Do not inherit the old hidden OEM1 `RoutingSingle` model as a separate UI concept.

---

## 10. `None` Is Removed From the Product Mapping Model

There is no valid front-button assignment of `None`.

Delete `None` from the new shared `FrontButtonAction` vocabulary.

Delete the old persisted action models that carry `None`:

```text
Contracts.Oem1.Oem1Action / Oem1MappingSettings slot model
Contracts.Wing.WingAction / WingMappingSettings slot model
runtime WingAction/WingMapping projection
```

where they are superseded by the new shared contract.

Do not keep a hidden user/persistence `None` merely to preserve an obsolete schema.

### 10.1 Remove persisted Double slots

The target product mapping is one action per physical button per domain.

Therefore remove persisted/user mapping slots for:

```text
NormalDouble
RoutingDouble
Wing Double
```

and the associated `Single` naming from the persisted model.

### 10.2 Gesture recognizers do not need a new redesign

The existing OEM1/WING recognizers already deliver an immediate Single gesture when double-click handling is disabled.

For this PR, configure their production mapping path with double-click disabled permanently.

Conceptually:

```csharp
doubleClickEnabled: false
```

or equivalent for both front buttons.

Then one physical press immediately resolves the active domain's one binding.

It is acceptable to retain low-level recognizer `Double` enum/code if deleting it would broaden this PR into unnecessary gesture-infrastructure cleanup.

The required deletion is the **persisted/product Double mapping model**, not a forced rewrite of every recognizer implementation detail.

Do not add another debounce/gesture manager.

---

## 11. Remove Obsolete OEM1 `RemappingEnabled`

The current Controller page already forces OEM1 remapping to `true`, and the Full1902 front-button owner is active only under Addon controller authority.

The final user model has no independent Center M Button remapping on/off switch.

Remove:

```text
Oem1MappingSettings.RemappingEnabled
SettingsStore salvage logic for RemappingEnabled
ControllerPage `mapping with { RemappingEnabled = true }`
comments/tests describing user-disable of OEM1 remapping
```

Do not replace it with another boolean.

Controller authority remains selected only by the Device-page MSI Center M Enable/Disable workflow.

---

## 12. Cross-Button Uniqueness Invariant

Within each domain:

```text
Gamebar Button Action != Center M Button Action
```

Required:

```text
Normal.Gamebar.Action != Normal.CenterM.Action
Steam.Gamebar.Action  != Steam.CenterM.Action
```

Not required:

```text
Normal.Gamebar.Action != Steam.CenterM.Action
Normal.CenterM.Action != Steam.Gamebar.Action
```

Cross-domain reuse is valid because only one domain is active at a time.

### 12.1 Equality means semantic Action value

The duplicate rule applies to the action itself, not its configuration.

Therefore these are invalid in the same domain even if their payload differs:

```text
Gamebar = Keyboard / Hotkey (Ctrl+F1)
Center M = Keyboard / Hotkey (Alt+F2)
```

and:

```text
Gamebar = Launch Application (A.exe)
Center M = Launch Application (B.exe)
```

The user's rule is that the two buttons may not select the same mapping value.

### 12.2 UI prevention

When one button uses an action in a domain, disable that action in the partner ComboBox for the same domain.

Example:

```text
Normal.Gamebar = Quick Settings Overlay
→ Normal.CenterM "Quick Settings Overlay" item disabled
```

Do not silently swap the two mappings.

Do not auto-change the other button when the user edits one.

### 12.3 Persistence/runtime guard

UI prevention is not sufficient.

A hand-edited settings file or a direct frontend RPC must not persist an invalid duplicate mapping.

Use the one shared mapping validation policy before persistence.

Do not introduce a second manager/authority solely for this rule.

---

## 13. Quick Settings Overlay Action

Add:

```text
Quick Settings Overlay
```

as a valid action in both domains and for both physical buttons.

Internal enum/member name may be:

```text
QuickSettingsOverlay
```

### 13.1 Required execution path

Physical event dispatch must route to the existing Runtime-owned coordinated overlay toggle seam.

Required shape:

```text
Event88 / Event41
→ existing front-button event/gesture path
→ resolve active-domain binding
→ QuickSettingsOverlay
→ AddonProcessHost coordinated Overlay toggle
→ existing Overlay lifecycle/capture path
```

Do not call directly into:

```text
OverlayProcessController.ShowAsync
OverlayProcessController.EnsureHiddenAsync
OverlayProcessController.ToggleForPocAsync
NamedPipeOverlayServer
OverlayWindow
```

from a button dispatcher.

### 13.2 Productize the host seam name

The current host entry point is named:

```text
ToggleOverlayForPoc()
```

This action is now a production user binding.

Rename the narrow host-facing method to a production name such as:

```text
RequestOverlayToggle()
```

or equivalent.

Keep `CoordinateOverlayToggleAsync()` as the single ordering/capture owner unless a small naming cleanup is useful.

Do not create `OverlayButtonManager`, `QuickSettingsActionManager`, or another visibility authority.

### 13.3 Overlay failure remains feature-local

Overlay process/start/show failure must continue to use the existing Overlay failure policy.

A failed Overlay request must not:

```text
change PID
release HidHide
switch X360/SteamDeck presentation
revoke Full1902 controller authority
recreate VIIPER
```

For OEM1, do not reinterpret a normal Overlay feature failure as a reason to restore MSI Center M authority.

Only actual synchronous dispatcher/infrastructure exceptions should follow the existing front-button failure semantics.

---

## 14. Runtime Dispatcher Changes

Keep the two physical event paths separate where their lifecycle safety differs:

```text
OEM1/Event41 path
WING/Event88 path
```

Do not merge their WMI/suppression authority into a generalized front-button runtime manager.

They may share:

```text
FrontButtonMappingSettings
FrontButtonAction
FrontButtonActionCapabilities
hotkey/application binding types
mapping validation
```

### 14.1 OEM1 / Center M Button

For each delivered physical press:

```text
capture actual SteamDeck presentation active
→ domain = Steam or Normal
→ binding = mapping.CenterM for that domain
→ validate action supports domain
→ dispatch
```

Actions:

```text
QuickSettingsOverlay → request coordinated Overlay toggle
SteamBigPicture      → existing Big Picture launcher
SteamButton          → existing TryRequestSteamPulse seam
SteamQuickAccess     → existing TryRequestQuickAccessPulse seam
KeyboardHotkey       → existing bounded keyboard executor
LaunchApplication    → existing bounded .exe launcher
```

Only domain-supported actions are reachable from valid persistence.

### 14.2 Gamebar Button / WING

Use the same domain resolution and action semantics.

Preserve:

```text
Event88 source
WinGSuppressionGuard readiness gate
WingRouteAuthoritySnapshot lifetime/epoch behavior where still needed
stale-delivery rejection
```

Do not weaken Policy B's rule that native Xbox Game Bar must not surface from the Gamebar Button while Addon controller authority is active.

The user-facing label `Gamebar Button` does not mean native Game Bar becomes an available action.

---

## 15. One Settings Authority and One Mutation Operation

Replace the split preference interfaces:

```text
IOem1MappingPreference
IWingMappingPreference
```

with one narrow read authority, conceptually:

```csharp
public interface IFrontButtonMappingPreference
{
    FrontButtonMappingSettings FrontButtonMapping { get; }
    event EventHandler? FrontButtonMappingChanged;
}
```

`StartupSettingsCoordinator` remains the one settings owner.

Replace:

```text
ChangeOem1Mapping(...)
ChangeWingMapping(...)
```

with one validated mutation:

```text
ChangeFrontButtonMapping(...)
```

or an equivalently narrow method.

Required order:

```text
validate complete candidate
→ write complete candidate to SettingsStore
→ update current Settings
→ publish one FrontButtonMappingChanged event
```

Save-then-publish remains the rule.

### Invalid candidate

An invalid candidate must:

```text
not write disk
not change current Settings
not publish changed event
```

Use a small explicit validation result or a clear exception contract consistent with the surrounding settings code.

Do not silently repair a user mutation into a different action pair.

---

## 16. AppSettings / SettingsStore Migration

Replace:

```text
AppSettings.Oem1Mapping
AppSettings.WingMapping
```

with:

```text
AppSettings.FrontButtonMapping
```

or an equivalently clear singular property.

### 16.1 Pre-release migration policy

The application is pre-release and the semantic model changed materially.

Do not build a schema migration framework for old button mappings.

On load:

```text
FrontButtonMapping missing
→ use the new frozen defaults

FrontButtonMapping malformed / unsupported / duplicate / incomplete
→ use the new frozen defaults for FrontButtonMapping only
→ preserve unrelated settings
```

Old pre-release JSON members:

```text
Oem1Mapping
WingMapping
```

may simply be ignored.

The next normal settings save drops them.

Do not attempt to infer a new Normal/Steam mapping from the old split/Single/Double structure.

### 16.2 Validation on load

The loader must validate:

```text
all four required bindings exist
all actions are recognized
all actions are supported in their domain
Normal pair is unique
Steam pair is unique
hotkey/application payload structures are non-null/parseable
```

Do not let malformed front-button JSON reset unrelated log/developer/overlay-tab settings.

---

## 17. Frontend Contract and Transport

The current frontend contract transports the old mapping records directly.

Replace:

```text
FrontendSettingsSnapshot.Oem1Mapping
FrontendSettingsSnapshot.WingMapping
```

with one:

```text
FrontendSettingsSnapshot.FrontButtonMapping
```

Replace bootstrap availability:

```text
Oem1MappingAvailable
WingMappingAvailable
```

with one hardware capability:

```text
FrontButtonMappingAvailable
```

Current production bootstrap already derives both old values from the same supported-hardware fact, so do not preserve two booleans without a real distinction.

Replace frontend mutation methods:

```text
SetOem1MappingAsync(...)
SetWingMappingAsync(...)
```

with one:

```text
SetFrontButtonMappingAsync(...)
```

Update:

```text
IAddonFrontendControl
InProcessAddonFrontendControl
NamedPipeAddonFrontendClient
NamedPipeAddonFrontendServer
FrontendWire RPC enum/request record
frontend contract tests
named-pipe transport tests
```

### Protocol bump

Bump exactly once:

```text
FrontendTransportProtocol.CurrentVersion
24 → 25
```

Add the usual protocol-history comment explaining that v25 replaces the split OEM1/WING mapping contract with the atomic front-button mapping contract.

No compatibility shim is required for this pre-release app.

Do not bump `.Overlay` protocol for this work: the Overlay action is an internal Runtime request and does not change the Overlay wire schema.

---

## 18. Controller UI

Target layout:

```text
Controller

[Button Mapping]

[Gamebar Button]
  Normal
    <Action ComboBox>
  Steam Game / Big Picture
    <Action ComboBox>

[Center M Button]
  Normal
    <Action ComboBox>
  Steam Game / Big Picture
    <Action ComboBox>
```

Exact SettingsCard/SettingsExpander nesting may follow the existing UI style.

### Required naming

User-visible strings:

```text
Gamebar Button
Center M Button
Normal
Steam Game / Big Picture
Quick Settings Overlay
Steam Big Picture
Steam Button
Steam Quick Access
Keyboard / Hotkey
Launch Application
```

Do not show `WING` as the primary user label.

Do not show `OEM1` as the primary user label.

### Required order

Always:

```text
Gamebar Button
Center M Button
```

### No `None`

No ComboBox may offer `None`.

No blank/unselected state is a valid steady UI state.

### Hotkey / application config

Reuse the current inline editor style:

```text
Keyboard / Hotkey selected
→ show modifiers/key configuration

Launch Application selected
→ show executable + arguments configuration

other action selected
→ hide configuration row
```

A small page-local slot-editor helper reused for the four visible bindings is appropriate.

Do not build a generalized settings-control framework.

---

## 19. One MainWindow Save Chain

The current MainWindow owns one ordered OEM1 save chain:

```text
_oem1UiMapping
_oem1PersistedMapping
_oem1SaveChain
_oem1EditVersion
QueueOem1Mutation(...)
```

Replace this with one ordered **whole front-button mapping** save chain.

Conceptually:

```text
_frontButtonUiMapping
_frontButtonPersistedMapping
_frontButtonSaveChain
_frontButtonEditVersion
QueueFrontButtonMutation(...)
```

Required behavior remains:

```text
UI edit occurs
→ update latest in-memory UI mapping synchronously
→ reflect it across both button editors immediately
→ enqueue one whole-record frontend save
→ stale save result may not visually roll back a newer edit
→ failed newest save rolls back to last known persisted whole mapping
```

This is important because the cross-button uniqueness rule belongs to one whole mapping.

Do not create separate Gamebar and Center M save chains.

That would recreate the same lost-update class the existing OEM1 chain was designed to eliminate.

---

## 20. UI Duplicate-Action Behavior

Each domain is one pair.

Example:

```text
Normal
  Gamebar  = Quick Settings Overlay
  Center M = Steam Big Picture
```

In the Center M Normal ComboBox:

```text
Quick Settings Overlay → disabled
```

If the user changes Gamebar Normal to `Keyboard / Hotkey`:

```text
Keyboard / Hotkey → immediately disabled in Center M Normal
Quick Settings Overlay → available again in Center M Normal
```

Do the same independently for Steam domain.

Do not:

```text
auto-swap actions
auto-select a replacement
show a modal dialog for ordinary valid editing
serialize transient duplicate state to Runtime
```

The partner action should be unavailable before the user can select it.

The settings/runtime validation remains the final guard.

---

## 21. Failure and Lifecycle Policy

This feature is configuration/dispatch, not controller authority.

Do not modify:

```text
Center M Enable/Disable authority state
PID1901/PID1902 ownership
HidHide baseline
DirectInput acquisition/recovery
VIIPER ownership/teardown
Steam/BPM detection
presentation attach/detach ordering
sleep/hibernate/recovery policy
```

### Actual presentation change while button UI is open

No special UI synchronization is required.

The UI edits both persisted domains regardless of which one is live.

Runtime chooses the correct domain fresh at button press time.

### Steam/BPM changes during a button press

Do not add epochs/barriers/state machines solely for a theoretical line-level crossing.

Capture the existing actual presentation fact once for the dispatch and execute that resolved binding.

### Overlay open/close

Overlay capture remains orthogonal to presentation selection.

Opening Quick Settings must not itself switch Normal/Steam mapping domain.

The domain continues to be determined by the active Full1902 presentation.

---

## 22. Tests — Required Coverage

Update/delete obsolete OEM1/WING mapping tests rather than preserving historical schema for test convenience.

### 22.1 Contract/default tests

Prove exact defaults:

```text
Normal.Gamebar  = QuickSettingsOverlay
Normal.CenterM = SteamBigPicture
Steam.Gamebar   = SteamButton
Steam.CenterM  = SteamQuickAccess
```

Prove `FrontButtonAction` has no `None` member.

Prove no persisted Double fields exist in the new mapping contract.

### 22.2 Capability tests

Normal supports exactly:

```text
QuickSettingsOverlay
SteamBigPicture
KeyboardHotkey
LaunchApplication
```

Steam supports exactly:

```text
QuickSettingsOverlay
SteamButton
SteamQuickAccess
KeyboardHotkey
LaunchApplication
```

### 22.3 Duplicate invariant tests

Reject:

```text
Normal.Gamebar == Normal.CenterM
Steam.Gamebar == Steam.CenterM
```

Allow the same action across different domains.

Explicitly test `KeyboardHotkey` and `LaunchApplication` equality is rejected even with different payload values.

### 22.4 SettingsStore tests

Cover:

```text
missing new mapping → new defaults
valid mapping round-trip
malformed mapping → mapping defaults only
unknown action → mapping defaults only
domain-invalid action → mapping defaults only
duplicate Normal pair → mapping defaults only
duplicate Steam pair → mapping defaults only
old Oem1Mapping/WingMapping JSON ignored
next save contains only new FrontButtonMapping
unrelated settings preserved
```

### 22.5 Settings coordinator tests

Prove:

```text
valid candidate → save, current state update, one changed event
invalid duplicate → no save, no state update, no event
invalid domain action → no save, no state update, no event
```

### 22.6 Runtime dispatch tests

Prove default execution:

```text
Normal + Gamebar  → Overlay request
Normal + Center M → Big Picture
Steam + Gamebar   → Steam pulse
Steam + Center M  → Quick Access pulse
```

Prove domain comes from actual SteamDeck presentation callback, not raw Steam demand.

Prove `QuickSettingsOverlay` works from either button in either domain when configured validly.

Prove domain-invalid persisted values are refused by runtime validation if a test bypasses SettingsStore.

Preserve Win+G suppression/authority tests.

### 22.7 Immediate single-press behavior

Because persisted Double mapping is removed, prove production recognizers are configured so one press is delivered immediately rather than waiting the old 200 ms double-click window.

Do not add timing-heavy race tests beyond the real recognizer contract.

### 22.8 Frontend contract/transport tests

Prove:

```text
FrontendSettingsSnapshot carries FrontButtonMapping
bootstrap carries FrontButtonMappingAvailable
SetFrontButtonMapping RPC round-trips
old SetOem1Mapping / SetWingMapping RPCs are gone
FrontendTransportProtocol.CurrentVersion == 25
```

Update all exact protocol assertions currently locked to 24.

### 22.9 UI architecture tests

Prove source/layout contains:

```text
Gamebar Button before Center M Button
Normal
Steam Game / Big Picture
```

and does not expose:

```text
WING
None
Remapping Enabled
Steam Input Routing
```

as normal Controller mapping UI labels.

Do not assert fragile pixel positions.

---

## 23. Likely Files to Change

This list is guidance, not a requirement to touch every file.

### Contracts

```text
src/SteamInputAddonforClaw.Contracts/FrontButtons/FrontButtonMapping.cs      [new]
src/SteamInputAddonforClaw.Contracts/Oem1/Oem1Mapping.cs                    [replace/delete obsolete mapping model]
src/SteamInputAddonforClaw.Contracts/Wing/WingMapping.cs                    [replace/delete obsolete mapping model]
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
```

### Runtime / settings

```text
src/SteamInputAddonforClaw/Settings/AppSettings.cs
src/SteamInputAddonforClaw/Settings/SettingsStore.cs
src/SteamInputAddonforClaw/Settings/StartupSettingsCoordinator.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs
src/SteamInputAddonforClaw/CenterM/Oem1ActionDispatcher.cs
src/SteamInputAddonforClaw/Wing/WingActionDispatcher.cs
src/SteamInputAddonforClaw/Wing/WingMapping.cs                               [likely delete]
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
```

Keep existing event sources/bridges/recognizers unless a mechanical signature cleanup is required.

### Frontend transport

```text
src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
```

### WinUI

```text
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs
src/SteamInputAddonforClaw.UI/Views/CenterMButtonPage.xaml                    [replace/rename as appropriate]
src/SteamInputAddonforClaw.UI/Views/CenterMButtonPage.xaml.cs                 [replace/rename as appropriate]
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
```

A focused replacement such as:

```text
Views/FrontButtonMappingPage.xaml
Views/FrontButtonMappingPage.xaml.cs
```

is acceptable and likely clearer than forcing the Gamebar editor into a control named `CenterMButtonPage`.

Do not build a reusable app-wide mapping framework.

### Tests

At minimum inspect/update:

```text
tests/SteamInputAddonforClaw.Tests/Oem1MappingSettingsPersistenceTests.cs
tests/SteamInputAddonforClaw.Tests/WingMappingSettingsPersistenceTests.cs
tests/SteamInputAddonforClaw.Tests/Oem1ActionCapabilitiesTests.cs
tests/SteamInputAddonforClaw.Tests/Oem1ActionDispatcherTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawFrontButtonRuntimeTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/UiArchitectureTests.cs
tests containing exact FrontendTransportProtocol v24 assertions
Full1902 WinG suppression tests
```

Prefer replacing obsolete schema tests with new product-model tests rather than retaining compatibility shells.

---

## 24. Documentation Update in the Implementation PR

Update the Controller/button-mapping section of:

```text
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
```

so the architecture no longer describes WING as a future/unfinalized UI item.

Document the final user terminology and defaults:

```text
Gamebar Button
Center M Button
Normal / Steam Game & Big Picture domains
Quick Settings Overlay / Big Picture / Steam Button / Quick Access defaults
```

Do not rewrite unrelated UI architecture sections.

---

## 25. Explicit Non-Goals

Do not include:

```text
M1/M2 mapping
Joystick LED
vibration-strength UI
rumble feedback implementation
gyro
fan control
battery charge limit
new Steam/BPM detector
new presentation manager
new Overlay manager
new controller authority state
native Game Bar re-enable action
macro/sequence actions
per-game front-button mapping
multi-user / RDP / Fast User Switching support
```

Do not touch Full1902 HidHide/controller lifecycle merely because the front-button action model changes.

---

## 26. Validation

Required before completion:

```text
Debug build clean
Release build clean
full Release test suite pass
frontend named-pipe protocol tests pass at v25
publish/layout verification pass
repository search confirms obsolete mapping contract names are gone where intended
```

Run repository searches for stale product semantics, including:

```text
RemappingEnabled
NormalDouble
RoutingDouble
WingMappingSettings
Oem1MappingSettings
SetWingMapping
SetOem1Mapping
Steam Input Routing
WING
None
ToggleOverlayForPoc
```

Do not mechanically delete every `WING`, `OEM1`, `Double`, or `None` occurrence in the entire repository: low-level hardware/gesture/history documents may legitimately retain those terms.

Classify each remaining production occurrence against this work order.

If supported MSI Claw hardware is available, smoke-test:

```text
Normal domain:
  Gamebar → Overlay show/hide
  Center M → Big Picture

Steam game/BPM domain:
  Gamebar → Steam Button
  Center M → Steam Quick Access

change one mapping:
  partner same-domain action becomes unavailable
  persistence survives UI restart

Overlay selected on alternate button/domain:
  same existing Overlay capture lifecycle is used
  no PID/presentation ownership regression
```

---

## 27. Acceptance Criteria

The PR is complete only when all of the following are true:

1. Controller UI shows `Gamebar Button` first and `Center M Button` second.
2. Both buttons expose `Normal` and `Steam Game / Big Picture` mappings.
3. Runtime domain selection uses actual `SteamDeck` presentation state, not a new Steam-mode setting.
4. Default Normal mapping is Gamebar=`Quick Settings Overlay`, Center M=`Steam Big Picture`.
5. Default Steam mapping is Gamebar=`Steam Button`, Center M=`Steam Quick Access`.
6. `None` is not a valid persisted/user front-button action.
7. Persisted Double mapping slots are removed.
8. OEM1 `RemappingEnabled` is removed; there is no replacement user toggle.
9. Both buttons use the same domain-specific action capability table.
10. Same-domain duplicate actions are impossible in UI and rejected by persistence validation.
11. Cross-domain reuse of the same action remains allowed.
12. `Quick Settings Overlay` routes through the existing `AddonProcessHost` coordinated Overlay toggle/capture path.
13. No button dispatcher directly owns Overlay process/window/transport lifecycle.
14. Existing WING Win+G suppression authority remains intact.
15. One atomic `FrontButtonMappingSettings` (or equivalent) is the persisted source of truth.
16. MainWindow uses one whole-mapping ordered save chain.
17. Old split OEM1/WING frontend mutation RPCs are removed.
18. Frontend protocol is exactly v25.
19. Old pre-release mapping JSON does not crash/reset unrelated settings; new defaults are used.
20. Full1902 controller/HidHide/VIIPER lifecycle behavior is unchanged.
21. Full Release tests pass.

---

## 28. Design Principle

The end state should be explainable in one sentence:

> **The Addon owns two MSI front buttons; each button has one Normal action and one Steam-presentation action, both are always assigned, each domain uses two different actions, and either button can invoke the same existing Quick Settings Overlay lifecycle when configured to do so.**

If an implementation requires a second state authority, a generalized mapping manager, a new Steam observer, or a second Overlay lifecycle owner to achieve this, it is over-designed and should be simplified before merge.
