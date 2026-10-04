# Work Order — Overlay Shared Production Controls Parity: Battery, LED, Vibration, and Current Power Source

> Date: 2026-10-04  
> Status: Ready for implementation  
> Scope: One focused PR  
> Repository baseline reviewed: `main` at `e01f1706fe273e63c24e1dcdc26d57e18990a1f0`  
> Product architecture: Standalone Full1902  
> Target surface: Addon WinUI3 Overlay  
> Architectural rule: extend the existing shared frontend/product paths only; no new architectural layer

---

## 1. Goal

Expose four already-production features in the Addon Overlay without creating any Overlay-owned feature implementation:

~~~text
Device
  Battery Charge Limit
    Enabled
    Limit 60..100%, step 5

Controller
  Joystick LED
    Enabled
    Brightness 0..100
    Color RGB

  Vibration Strength
    Left Motor  0..100
    Right Motor 0..100
    NO Test buttons

Setting
  Quick Settings
    Show only current power source
~~~

This is a parity PR, not a new hardware-feature PR.

The Runtime, persistence, Full1902 ownership, hardware apply, and lifecycle policies already exist.

The work is to connect those existing authorities to the existing shared frontend / Overlay composition with the smallest possible extensions.

---

## 2. Non-negotiable architecture rule

The user requirement for this PR is:

> If a feature is missing from the current shared frontend path, add it to the existing shared structure.  
> Do not implement a second Overlay-specific feature path.  
> Do not create a new architectural layer.

Allowed:

~~~text
existing production authority
        ↓
existing shared frontend contract / existing shared product projection
        ↓
existing Overlay transport
        ↓
Overlay renderer
~~~

Forbidden:

~~~text
OverlayBatteryManager
OverlayControllerSettingsManager
OverlayLedManager
OverlayVibrationManager
OverlaySettingsProvider
ControllerQuickSettingsManager
new facade
new coordinator
new provider/registry
new state authority
new persistence file
Overlay-only settings cache as product truth
direct EC/HID/hardware calls from Overlay
parallel DTOs that duplicate existing shared frontend DTOs
generic "form engine" or feature framework
~~~

A narrow transport correlation wrapper is allowed when the existing Overlay named pipe needs request correlation. It is transport framing only; it must carry the existing shared product DTO/value and must not become a second product contract.

Do not refactor unrelated existing Overlay feature paths merely for architectural symmetry.

---

## 3. Required reading before implementation

### 3.1 Full1902 authority

Read in the precedence defined by the Full1902 README:

~~~text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
~~~

Preserve:

~~~text
Center M Enabled
-> MSI / stock controller authority
-> desired PID1901
-> Addon must not apply LED/vibration controller settings

Center M Disabled
-> Addon Runtime controller authority
-> desired PID1902
-> Addon owns DirectInput / HidHide / VIIPER controller stack
-> existing LED/vibration apply policies may operate

Steam/BPM
-> virtual presentation only
-> must not become LED/vibration ownership authority
~~~

### 3.2 Shared frontend / Overlay architecture

Read:

~~~text
docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
~~~

Important current architecture fact:

- Device/Profile use the generic `QuickSettingsPageSnapshot` contract.
- Controller/Setting are typed special pages and must not be forced into a generic Device/Profile page model merely for this PR.
- The WinUI3 Overlay is the active Addon-owned Quick Settings surface. Historical QamHost/CEF implementation is not a reason to add new QAM-specific work.

### 3.3 Feature authority references

Read:

~~~text
docs/work-order/FULL1902_CONTROLLER_LED_STATIC_BASIC_WORK_ORDER_2026-10-03.md
docs/work-order/FULL1902_CONTROLLER_VIBRATION_PRODUCTION_PERSISTENCE_LIFECYCLE_WORK_ORDER_2026-10-04.md
docs/work-order/ADDON_QUICK_SETTINGS_CURRENT_POWER_SOURCE_VISIBILITY_WORK_ORDER.md
~~~

For vibration, the 2026-10-04 production-persistence work order is the current authority over the older firmware-readback design.

Current production vibration truth is:

~~~text
settings.json ControllerVibration
-> desired Left/Right pair
-> one contiguous PID1902 profile write
-> no SyncToROM
~~~

Do not restore the older "firmware readback is persistent truth" model.

---

## 4. Current code facts confirmed at the reviewed baseline

### 4.1 Device shared Quick Settings currently omits Battery Charge Limit

Current:

~~~text
FrontendDeviceQuickSettingsSnapshot
  CpuBoost
  Tdp
  PowerMode
~~~

and:

~~~text
QuickSettingsPresentation.BuildDevice(...)
  TDP
  CPU Boost
  Windows Power Mode
~~~

However `DevicePage.RefreshAsync()` already has production Battery Charge Limit and currently performs a second focused capture:

~~~text
CaptureDeviceQuickSettingsAsync()
-> CPU / TDP / Power

CaptureBatteryChargeLimitAsync()
-> Battery
~~~

This is the one feature in this work order that is genuinely missing from the existing Device shared aggregate/product projection.

Therefore Battery must be added to that existing shared path.

Do not create an Overlay Battery path.

### 4.2 Battery production authority already exists

Reuse without redesign:

~~~text
FrontendBatteryChargeLimitSnapshot
FrontendBatteryChargeLimitMutationResult

IAddonFrontendControl.CaptureBatteryChargeLimitAsync
IAddonFrontendControl.SetDeviceBatteryChargeLimitEnabledAsync
IAddonFrontendControl.SetDeviceBatteryChargeLimitPercentAsync

MsiClawBatteryChargeLimitRuntime
~~~

Current Main UI semantics include:

~~~text
Enabled
Limit = 60..100
Step = 5
persisted desired state
actual hardware state kept distinct
normal lifecycle reconcile owned by Runtime
~~~

### 4.3 LED shared frontend authority already exists

Reuse:

~~~text
ControllerLedSettings
FrontendSettingsSnapshot.ControllerLed
FrontendBootstrapSnapshot.ControllerLedAvailable
IAddonFrontendControl.SetControllerLedSettingsAsync(...)
~~~

Do not add another LED settings record for the Overlay.

Current persisted contract remains:

~~~text
Enabled
Brightness 0..100
Red
Green
Blue
~~~

The Overlay must preserve arbitrary RGB values. Do not replace the shared RGB contract with a lossy preset-only persistence model.

### 4.4 Vibration shared frontend authority already exists

Reuse:

~~~text
ControllerVibrationSettings
FrontendControllerVibrationStrengthSnapshot
FrontendControllerVibrationStrengthMutationResult

IAddonFrontendControl.CaptureControllerVibrationStrengthAsync(...)
IAddonFrontendControl.SetControllerVibrationStrengthAsync(left, right, ...)
~~~

The user-facing Overlay product is:

~~~text
Left Motor  0..100
Right Motor 0..100
~~~

Do not expose:

~~~text
Test Left
Test Right
profile-write probe
rumble-loop diagnostics
any Developer vibration operation
~~~

### 4.5 Current-power-source preference already exists

Reuse:

~~~text
FrontendSettingsSnapshot.QuickSettingsCurrentPowerSourceOnly
IAddonFrontendControl.SetQuickSettingsCurrentPowerSourceOnlyAsync(...)
QuickSettingsPresentation.ApplyPowerSourceVisibility(...)
InProcessAddonFrontendControl.NotifyQuickSettingsPowerSourceChanged()
~~~

Do not add another preference.

The existing rule remains:

~~~text
OFF
-> compact Device/Profile Quick Settings carry both AC and DC rows

ON + AC
-> show AC rows

ON + DC
-> show DC rows

ON + unknown power source
-> fail open for presentation: show both sides
~~~

The full Main UI Device/Profile editors continue showing both sides.

---

## 5. PR scope

Implement all four controls in one PR.

### In scope

~~~text
Battery into existing Device shared aggregate
Battery into existing Device QuickSettingsPageSnapshot product
Battery mutation through existing QuickSettingsMutationAdapter
Overlay Device renders Battery automatically through existing generic page renderer

Overlay Controller LED using existing ControllerLedSettings / settings authority
Overlay Controller vibration using existing vibration snapshot/mutation authority
Overlay Setting current-power-source toggle using existing settings authority

minimum Overlay transport additions needed to carry existing shared DTOs
focused protocol version bumps
tests
~~~

### Out of scope

~~~text
new hardware behavior
new persistence
Center M authority changes
PID1901/PID1902 lifecycle redesign
HidHide / VIIPER changes
M1/M2 redesign
front-button mapping changes
Vibration Test buttons in Overlay
Developer diagnostics
fan control
Battery test/probe UI
Steam FSE
app update controls
Required Components
QamHost resurrection
new generic Controller/Setting page framework
~~~

---

## 6. Device — move Battery Charge Limit into the existing shared Device path

### 6.1 Extend the existing aggregate

Extend `FrontendDeviceQuickSettingsSnapshot`.

Conceptually:

~~~csharp
public sealed record FrontendDeviceQuickSettingsSnapshot(
    FrontendCpuBoostSnapshot CpuBoost,
    FrontendTdpSnapshot Tdp,
    FrontendPowerModeSnapshot PowerMode,
    FrontendBatteryChargeLimitSnapshot BatteryChargeLimit);
~~~

Update `Unavailable` accordingly.

In `InProcessAddonFrontendControl.CaptureDeviceQuickSettingsAsync`, capture Battery as the fourth independent child using the same failure isolation already used for CPU/TDP/Power.

Required behavior:

~~~text
CPU capture fails    -> CPU unavailable, Battery/TDP/Power survive
Battery capture fails -> Battery unavailable, CPU/TDP/Power survive
~~~

No cross-feature transaction, lock, epoch, or parallel capture framework.

### 6.2 Main UI must consume the extended aggregate

Update the normal `DevicePage.RefreshAsync()` path so Battery comes from:

~~~text
FrontendDeviceQuickSettingsSnapshot.BatteryChargeLimit
~~~

Remove the extra normal-page `CaptureBatteryChargeLimitAsync()` call from that refresh path.

Keep the focused `CaptureBatteryChargeLimitAsync()` frontend method itself because it remains a valid typed production seam and may be used by other callers/tests.

This PR is about converging the normal Device product read, not deleting useful focused APIs.

### 6.3 Extend the existing Quick Settings product contract

Append new identities; do not reorder existing enum members because they cross process boundaries.

Add conceptually:

~~~text
QuickSettingsSectionId
  + DeviceBatteryChargeLimit

QuickSettingsRowId
  + DeviceBatteryChargeLimitEnabled
  + DeviceBatteryChargeLimitPercent
~~~

Do not create a Battery-specific Overlay row contract.

### 6.4 Device page order

The shared Device compact page should match the current Main UI product order:

~~~text
1. Battery Charge Limit
2. TDP Control
3. CPU Boost
4. Windows Power Mode
~~~

### 6.5 Battery projection semantics

Project:

~~~text
Battery Charge Limit
  Enabled       Toggle
  Limit         Numeric slider
~~~

Limit:

~~~text
Minimum = 60
Maximum = 100
Step    = 5
Suffix  = "%"
~~~

Writability:

~~~text
Enabled row:
  Available  = snapshot.Available
  Writable   = snapshot.Available
               && snapshot.PersistenceWritable
               && snapshot.Initialized

Limit row:
  Available  = snapshot.Available
  Writable   = snapshot.Available
               && snapshot.PersistenceWritable
~~~

The Limit row stays writable before initialization because selecting a valid limit is how the existing production UI takes ownership/initializes the feature.

Display value priority must follow existing Device-page semantics:

~~~text
DesiredLimitPercent
-> valid CurrentLimitPercent
-> 60 presentation draft fallback
~~~

The 60 fallback is presentation-only. It must not persist or imply ownership until the user commits a value.

If `LastFailure` exists, carry it through the existing section/page failure/message semantics rather than inventing an Overlay error store.

Battery rows are not AC/DC-specific and must remain visible regardless of `QuickSettingsCurrentPowerSourceOnly`.

### 6.6 Battery mutation dispatch

Extend the existing `QuickSettingsMutationAdapter` Device switch only.

~~~text
DeviceBatteryChargeLimitEnabled
-> validate one Boolean
-> SetDeviceBatteryChargeLimitEnabledAsync

DeviceBatteryChargeLimitPercent
-> validate one Integer
-> require 60..100 AND multiple of 5
-> SetDeviceBatteryChargeLimitPercentAsync
~~~

Every mutation result must re-project a fresh authoritative Device page exactly like the current Device Quick Settings mutations.

Do not add a Battery mutation manager or direct Runtime/hardware call.

---

## 7. Controller — Joystick LED

Controller is already a typed special page.

Do not convert Controller into `QuickSettingsPageId.Controller` or add a generic controller form schema in this PR.

Use the existing shared LED contracts directly.

### 7.1 Overlay product

Add below the existing M1/M2 section:

~~~text
Joystick LED
  Enabled
  Brightness
  Color
~~~

Recommended section order:

~~~text
M1 / M2
Joystick LED
Vibration Strength
~~~

### 7.2 State source

The authoritative user setting remains:

~~~text
FrontendSettingsSnapshot.ControllerLed
~~~

Availability remains the existing derived presentation fact:

~~~text
FrontendBootstrapSnapshot.ControllerLedAvailable
~~~

Do not persist availability.

Do not create:

~~~text
OverlayControllerLedSnapshot
OverlayLedSettings
~~~

if the existing shared types can be transported directly.

The Overlay may keep the last received shared snapshot as local renderer state, exactly as it already does for other pages. That local copy is presentation state only and never authority.

### 7.3 Mutation source

Every LED edit must call the existing:

~~~text
IAddonFrontendControl.SetControllerLedSettingsAsync(ControllerLedSettings)
~~~

Construct the new whole record from the latest authoritative shared settings snapshot and change only the edited field.

Examples:

~~~text
Enabled change
-> preserve Brightness/R/G/B

Brightness change
-> preserve Enabled/R/G/B

Color change
-> preserve Enabled/Brightness
~~~

Use the returned `FrontendSettingsSnapshot` as authoritative readback.

Do not optimistically treat the submitted value as final state when the Runtime returned a different snapshot.

### 7.4 Brightness

~~~text
0..100
step 1
~~~

Follow current Main UI behavior:

~~~text
Enabled=false
-> brightness/color remain remembered
-> editors may be visually disabled as appropriate
-> turning LED back on restores saved brightness/color
~~~

Do not model Off by changing saved brightness to zero.

### 7.5 Color

The shared product value remains exact RGB.

Overlay renderer may use a compact WinUI color flyout/picker, but it must round-trip arbitrary existing RGB values.

Do not create a preset-only persistence model.

Avoid issuing a hardware/settings mutation for every transient `ColorChanged` event while the picker is being dragged.

Allowed renderer-local behavior:

~~~text
open picker
-> edit local color draft
-> commit once on explicit accept / bounded picker close
-> SetControllerLedSettingsAsync(...)
-> render returned authoritative settings
~~~

This local draft is UI state only.

No new LED debounce service/timer abstraction.

---

## 8. Controller — Vibration Strength

### 8.1 Overlay product

Add:

~~~text
Vibration Strength
  Left Motor   0..100
  Right Motor  0..100
~~~

Keep Left and Right separate.

Do not combine them into one slider.

Do not add Test buttons.

### 8.2 State source

Use:

~~~text
CaptureControllerVibrationStrengthAsync()
-> FrontendControllerVibrationStrengthSnapshot
~~~

Current production source of truth remains the persisted `ControllerVibrationSettings` pair.

Overlay open/refresh must not perform firmware profile reads.

### 8.3 Mutation source

Use only:

~~~text
SetControllerVibrationStrengthAsync(leftPercent, rightPercent)
~~~

An edit to one motor must preserve the latest authoritative value of the other motor.

Example:

~~~text
current Left=35, Right=70
user changes Left to 40
-> submit 40,70
~~~

Do not derive the sibling from a default such as 50.

Use the returned `FrontendControllerVibrationStrengthMutationResult.Snapshot` as readback.

### 8.4 Commit behavior

Both values are 0..100, step 1.

Do not write on every key-repeat/pointer tick if the current Overlay value-row control already has a bounded commit/edit pattern.

Use the existing Overlay row mechanics and keep any draft local to the renderer.

Do not introduce a vibration commit manager.

### 8.5 Lifecycle prohibition

Overlay activity itself must perform zero controller-profile writes.

Specifically, no vibration reapply merely because of:

~~~text
Overlay process start
Overlay Show
Overlay Hide
Controller tab selection
settings state refresh
Xbox360 <-> SteamDeck presentation change
Steam game start/end
Big Picture enter/exit
~~~

Only a user vibration edit calls the existing mutation path.

Existing Runtime ownership/recovery/resume reapply remains untouched.

---

## 9. Setting — Show only current power source

### 9.1 UI placement

Current Setting page has expandable cards for ClawHUD and Tab Order.

Add one small card between them:

~~~text
ClawHUD

Quick Settings
  Show only current power source    On/Off

Tab Order
~~~

Extend the current `SettingCardId` / row-composition implementation directly.

Do not create a new Settings page framework.

### 9.2 State and mutation

Use existing shared state:

~~~text
FrontendSettingsSnapshot.QuickSettingsCurrentPowerSourceOnly
~~~

and existing mutation:

~~~text
SetQuickSettingsCurrentPowerSourceOnlyAsync(bool)
~~~

Use the returned `FrontendSettingsSnapshot` to render the authoritative result.

Do not create another persisted preference.

### 9.3 Refresh effect

The existing mutation already raises frontend invalidation.

Preserve that path so a successful toggle causes the currently visible compact Device/Profile state to converge through the existing refresh publication.

Do not directly hide/show Device/Profile rows inside the Setting event handler.

The flow must remain:

~~~text
Setting toggle
-> existing Runtime settings mutation
-> existing StateInvalidated
-> fresh Device/Profile Quick Settings capture
-> ApplyPowerSourceVisibility(...)
-> Overlay receives fresh authoritative pages
~~~

No polling.

No direct power-status watcher in the Overlay.

---

## 10. Overlay transport — extend only the current pipe

The Overlay has one existing Runtime-owned named-pipe transport.

Use it.

Do not add a second pipe or a new frontend client inside the Overlay process.

### 10.1 Battery

Battery requires no Battery-specific Overlay message.

It travels inside the existing:

~~~text
QuickSettingsPageSnapshot(Device)
QuickSettingsMutationIntent
QuickSettingsMutationResult
~~~

after section/row contract extension.

### 10.2 LED / current-power-source

Prefer transporting the already-existing shared `FrontendSettingsSnapshot` rather than creating an Overlay copy of LED/current-power-source state.

A single settings-state publication can supply:

~~~text
ControllerLed
QuickSettingsCurrentPowerSourceOnly
~~~

Do not use unrelated fields as new product features just because they are present in the shared snapshot.

For mutations, carry the existing shared values:

~~~text
ControllerLedSettings
bool currentPowerSourceOnly
~~~

through narrow correlated transport frames and call the existing frontend methods in Runtime.

### 10.3 Vibration

Transport the existing:

~~~text
FrontendControllerVibrationStrengthSnapshot
FrontendControllerVibrationStrengthMutationResult
~~~

Do not define an Overlay-specific vibration snapshot/result that duplicates them.

### 10.4 Existing M1/M2 transport stays

Current Overlay protocol v13 already has the dedicated M1/M2 mapping path.

Do not migrate or redesign it in this PR.

This PR is not a transport-convergence cleanup.

---

## 11. Runtime binding

Continue the current pattern where `AddonProcessHost` binds narrow delegates onto the existing `OverlayProcessController`.

Do not expose:

~~~text
StartupSettingsCoordinator
MsiClawBatteryChargeLimitRuntime
MsiClawRawHidTransport
MsiClawVibrationStrengthClient
physical ownership objects
~~~

to FrontendTransport or Overlay.

All delegates must terminate at the existing `IAddonFrontendControl` APIs.

Conceptually:

~~~text
Overlay request
-> existing Overlay pipe server
-> AddonProcessHost bound delegate
-> one existing IAddonFrontendControl method
-> existing Runtime/settings/hardware authority
~~~

If multiple new delegates are required, add the small fixed delegates directly.

Do not introduce a provider registry, command bus, generic feature dispatcher, or DI hierarchy to avoid a few explicit fields.

---

## 12. Overlay refresh/publication

The visible Overlay must receive fresh state for:

~~~text
Device shared Quick Settings
Controller LED settings
Controller vibration strength
Setting current-power-source preference
~~~

Use the existing event-driven visible-session refresh model.

One feature capture failing must be feature-local.

Examples:

~~~text
vibration capture failure
-> vibration rows unavailable
-> M1/M2 and LED remain usable

settings capture failure
-> LED/current-power-source fail closed
-> Device/Profile publication continues

Device Battery child capture failure
-> Battery unavailable
-> TDP/CPU/Power survive
~~~

Do not build a cross-feature transaction.

Do not add an epoch/barrier solely to make all cards refresh atomically.

---

## 13. Protocol versions

Current reviewed values:

~~~text
FrontendTransportProtocol.CurrentVersion = 48
OverlayTransportProtocol.CurrentVersion  = 13
~~~

This PR changes shared serialized frontend state and extends the Overlay wire.

Expected:

~~~text
FrontendTransportProtocol.CurrentVersion = 49
OverlayTransportProtocol.CurrentVersion  = 14
~~~

Append enum members; do not reorder existing serialized enum members.

No compatibility shim is required for this pre-release same-package client/server model.

A mixed old/new peer must fail the normal protocol handshake rather than silently ignore the new contract.

If either protocol version has legitimately moved before implementation starts, increment from the then-current value instead of forcing 49/14.

---

## 14. Overlay rendering details

### Device

Battery must use the existing generic Device Quick Settings renderer.

Do not hand-build a second Battery card in `OverlayWindow`.

### Controller

Extend the existing `BuildControllerPage` composition.

Keep existing M1/M2 behavior unchanged.

Use the current Overlay row/chrome/navigation primitives for LED/vibration.

Do not create a second controller-page renderer framework.

### Setting

Extend the existing expandable-card implementation in `OverlayWindow.ClawHud.cs` (or its current equivalent).

The new Quick Settings card participates in:

- controller row selection;
- pointer selection;
- Back/collapse behavior;
- show/reset behavior;

using the same mechanisms as ClawHUD/Tab Order.

Update the existing row-index/remap calculations that currently assume exactly two Setting cards.

Do not hard-code a fragile `+2` card-header offset if the new third card makes that assumption incorrect. Keep the calculation local and explicit; do not create a generalized layout-index service.

---

## 15. Failure semantics

### Battery

Follow existing `FrontendBatteryChargeLimitMutationOutcome`.

Persistence failure vs apply failure remains meaningful.

Never overwrite persisted desired state from an observed hardware value merely to make the Overlay look converged.

### LED

`SetControllerLedSettingsAsync` persists desired state first and applies through the existing best-effort callback.

A transient hardware apply failure must not cause the Overlay to create a second rollback authority.

Render the Runtime-returned settings and let existing lifecycle reapply policy remain authoritative.

### Vibration

Use existing vibration outcome/snapshot.

Do not fabricate success.

Do not invoke Test as a verification mechanism.

### Current power source

On transport/mutation failure, re-render the last authoritative value or fresh Runtime readback using the same fail-closed pattern already used by Main UI settings.

Do not leave an optimistic toggle state as truth.

---

## 16. Tests

Add/adjust focused tests only.

### 16.1 Device aggregate

Verify:

- `FrontendDeviceQuickSettingsSnapshot` contains Battery;
- Battery capture is independently failure-isolated;
- Main UI normal Device refresh no longer performs a second Battery capture;
- existing focused Battery methods remain available.

### 16.2 Device Quick Settings product

Verify:

~~~text
Battery section order is first
Enabled row is Toggle
Limit row = 60..100, step 5, "%"
Enabled is not writable before initialization
Limit remains writable when available+persistence-writable
Battery rows are unaffected by AC/DC visibility filtering
~~~

### 16.3 Device mutation adapter

Verify:

- valid enable dispatches exactly once;
- valid 60/65/.../100 limit dispatches exactly once;
- 59, 61, 101, wrong value kind, duplicate/malformed intent dispatch zero mutations;
- returned page is fresh authoritative state.

### 16.4 Controller LED

Verify:

- Overlay shows Enabled/Brightness/Color only when existing LED availability allows it;
- each edit preserves untouched fields;
- arbitrary RGB round-trips;
- Off preserves stored brightness/color;
- Overlay contains no HID/raw-device calls;
- no new LED settings type/persistence exists.

### 16.5 Controller vibration

Verify:

- Left and Right are separate;
- range 0..100;
- edit of Left preserves current Right;
- edit of Right preserves current Left;
- mutation calls existing `SetControllerVibrationStrengthAsync`;
- Overlay source contains no call/reference to `TestControllerVibrationMotorAsync`;
- Overlay show/hide/state refresh does not trigger vibration apply.

### 16.6 Setting

Verify:

- Quick Settings card appears between ClawHUD and Tab Order;
- toggle renders `FrontendSettingsSnapshot.QuickSettingsCurrentPowerSourceOnly`;
- mutation calls existing `SetQuickSettingsCurrentPowerSourceOnlyAsync`;
- returned authoritative value is rendered;
- existing AC/DC visibility tests still pass;
- Setting row selection and expand/collapse remain correct with three cards.

### 16.7 Transport

Verify:

- Frontend v49 rejects v48 peer;
- Overlay v14 rejects v13 peer;
- new frames carry existing shared DTOs rather than duplicate Overlay DTOs;
- malformed request fails locally and invokes zero Runtime mutation;
- one feature publication failure does not suppress unrelated feature publication.

### 16.8 Lifecycle regression

Existing tests must continue proving:

- no controller hardware writes while Center M authority is Enabled;
- PID1902 ownership/recovery remains the only physical-controller authority;
- LED/vibration lifecycle reapply remains unchanged;
- Steam/BPM presentation switches perform zero new LED/vibration apply;
- suspend/resume safety remains unchanged;
- Overlay capture/show/hide ordering remains unchanged.

Do not add synchronization solely for theoretical instruction-level races.

---

## 17. Files expected to change

Exact test filenames may follow the current suite, but implementation should remain concentrated around existing owners.

Likely production files:

~~~text
src/SteamInputAddonforClaw.Contracts/Frontend/FrontendContracts.cs
src/SteamInputAddonforClaw.Contracts/Frontend/QuickSettingsContracts.cs

src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsPresentation.cs
src/SteamInputAddonforClaw/Frontend/QuickSettingsMutationAdapter.cs

src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Lifecycle/OverlayProcessController.cs

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/OverlayWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayServer.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeOverlayClient.cs

src/SteamInputAddonforClaw.Overlay/App.xaml.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.Controller.cs
src/SteamInputAddonforClaw.Overlay/OverlayWindow.ClawHud.cs
~~~

Do not create a new project.

Do not create a new Runtime service or manager.

If the implementation can avoid changing a listed file because the existing transport helper already handles the required shared DTO generically, prefer the smaller diff.

---

## 18. Acceptance criteria

The PR is complete only when all are true:

1. Overlay Device shows Battery Charge Limit through the existing generic Device Quick Settings path.
2. Main UI normal Device refresh and Overlay Device projection use the same extended `FrontendDeviceQuickSettingsSnapshot`.
3. Battery has Enable + 60..100 step-5 control with current production authority semantics.
4. Overlay Controller shows Joystick LED: Enabled, Brightness, exact RGB Color.
5. Overlay Controller shows separate Left/Right Vibration Strength controls.
6. Overlay Controller exposes no vibration Test operation.
7. Overlay Setting has a Quick Settings card with `Show only current power source`.
8. That toggle changes the existing persisted preference and Device/Profile refresh through existing invalidation.
9. LED uses existing `ControllerLedSettings` and `SetControllerLedSettingsAsync`.
10. Vibration uses existing `FrontendControllerVibrationStrengthSnapshot` and `SetControllerVibrationStrengthAsync`.
11. No Overlay feature code directly accesses EC/HID/firmware/controller ownership objects.
12. No new manager/facade/coordinator/provider/registry/state authority is introduced.
13. No new persistence mechanism or duplicate settings record is introduced.
14. Existing M1/M2, ClawHUD, Tab Order, Shortcut, Device/Profile behavior remains intact.
15. Full1902 physical ownership/lifecycle behavior is unchanged.
16. Protocol mismatches fail closed.
17. Tests pass.

---

## 19. Explicit anti-overengineering review checklist

Before adding any new class, ask:

> Does this class protect an actual supported product lifecycle failure, or is it only wrapping an existing shared method?

If it only wraps an existing shared method, do not add it.

Do not add:

~~~text
epoch
barrier
generation authority
cross-feature lock
retry state machine
generic command bus
generic setting registry
generic controller feature schema
new lifecycle watcher
new hardware abstraction
~~~

for this parity work.

Normal existing owner/gate/invalidation behavior is sufficient when it converges safely under the supported lifecycle.

The desired end state is not fewer lines at any cost.

It is:

> one existing Runtime authority per feature, one existing shared frontend representation, one existing Overlay transport, and thin renderer code.
