# Work Order — App UI PR-A: Navigation and Page Ownership Reorganization

> **Date:** 2026-09-04  
> **Status:** Ready for implementation  
> **Reviewed baseline:** `main` at `32fa738be413a79c41d7be002adf83a67930add3` (`Full1902 Cleanup J`, PR #485)  
> **Design authority:** `docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md`

---

## 1. Goal

Reorganize the main WinUI 3 application around the current Full1902 product model without changing controller lifecycle behavior.

The user-facing navigation must become:

```text
Device
Controller
Profile
How to Use

----------------
Settings
```

This PR removes the normal `Status` destination, makes `Device` the initial page, moves the useful Status information to its proper owner pages, moves MSI Center M controller-authority control from `Controller` to `Device`, removes the obsolete user-facing Windows-startup toggle, and keeps the existing Developer Menu under Settings.

This is an information-architecture/UI ownership PR.

It must **not** redesign Full1902 controller authority, HidHide, PID1902 ownership, DirectInput, VIIPER, Steam/BPM presentation selection, or reboot-bound transitions.

---

## 2. Current Product Authority

Read these documents before implementation:

```text
docs/Full 1902 Implementation/README.md
docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md
docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md
docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
```

Current product model:

```text
Center M Enabled
→ MSI / stock controller authority
→ PID1901 desired

Center M Disabled
→ Addon Runtime controller authority
→ PID1902 desired continuously
→ Addon Runtime/background startup is mandatory lifecycle infrastructure

Steam/BPM inactive
→ Xbox360 presentation

Steam/BPM active
→ SteamDeck presentation
```

The UI must not recreate the removed Steam-routing preference model or introduce another controller-authority state.

---

## 3. Current Source State Verified Before This Work Order

The work order was written against current `main`, not against the earlier UI design snapshot.

### 3.1 `MainWindow.xaml`

Current normal navigation order is:

```text
Status
How to Use
Controller
Device
Profile
```

The window currently instantiates:

```text
StatusPage
DevicePage
ProfilePage
HowToUsePage
ControllerPage
SettingsPage
DeveloperPage
VibrationTestPage
ClawSensorProbePage
FanHardwareProbePage
```

### 3.2 `MainNavigationState.cs`

Current default is:

```csharp
CurrentPage = MainNavigationPage.Status;
```

and unknown top-level navigation falls back to `Status`.

### 3.3 `StatusPage`

Current Status UI owns three remaining visible information groups:

```text
Device identity / support
Steam Game
Routing Components:
  HidHide
  usbip-win2
  VIIPER
```

The current Status page also provides a manual Refresh button and a warning InfoBar.

### 3.4 `MainWindow.xaml.cs`

`MainWindow` currently owns the authoritative frontend status refresh path:

```text
_frontend.StateInvalidated
→ RequestStatusRefresh()
→ RefreshSystemStatusAsync()
→ _frontend.CaptureStatusAsync()
→ RenderSystemStatus(snapshot)
```

This path also drives the window-level prerequisite setup prompt and setup/reboot UX.

Therefore **removing the Status page must not remove the status refresh pipeline itself**.

The frontend status snapshot remains useful as an input for:

- Device identity/support display;
- Settings Required Components display;
- prerequisite installation prompt/evaluation.

### 3.5 `ControllerPage`

Current Controller page mixes two unrelated responsibilities:

```text
MSI Center M controller-authority Enable/Disable transition
OEM1 / Center M button mapping
```

The controller-authority card includes important reboot-bound safety/presentation behavior and must be moved, not rewritten.

### 3.6 `DevicePage`

Current Device page already owns:

```text
TDP Control
CPU Boost
Windows Power Mode
```

Its `Activate()` / `Deactivate()` lifecycle and `StateInvalidated` subscription are already the natural refresh owner for device-level settings.

### 3.7 `SettingsPage`

Current Settings page contains only:

```text
Launch at Windows startup
Developer Menu
```

The startup toggle is no longer a valid user-facing preference in the target UI.

### 3.8 Cleanup J note

Current `main` has already merged Full1902 Cleanup J.

The Developer Vibration Test transport/runtime RPC stack was removed and the Developer Vibration Test page is now only a static unavailable shell.

Do **not** reintroduce vibration transport, feedback authority, vibration sessions, or related frontend RPCs while touching Developer/Settings navigation.

---

## 4. Target Navigation

Change the normal NavigationView menu to exactly:

```text
Device
Controller
Profile
How to Use
```

Keep the standard NavigationView Settings destination.

### Required behavior

```text
Initial selected item = Device
MainNavigationState initial page = Device
```

`Status` must no longer appear as a normal page or navigation destination.

Do not create:

```text
Home
Dashboard
Overview
Summary
```

as a replacement.

### Unknown navigation tag

Do not preserve `Status` as an implicit fallback.

Use the smallest safe fallback consistent with the target shell, preferably `Device`.

Do not add a separate navigation router/manager/state machine for this change.

---

## 5. Remove Status From the Visible Shell, But Preserve the Status Snapshot Pipeline

### 5.1 Remove from `MainWindow.xaml`

Remove:

```text
Status NavigationViewItem
StatusContent / StatusPage instance
```

The `StatusPage.xaml` / `.xaml.cs` source files themselves may remain in the project for PR-B cleanup.

This PR does not need to perform the final legacy-source deletion pass.

### 5.2 Remove Status page switching

`MainWindow.ShowPage(...)` must no longer contain a visible `MainNavigationPage.Status` branch.

Remove the `Status` enum member from `MainNavigationPage` in this PR because it is no longer a valid navigation destination.

### 5.3 Preserve `RefreshSystemStatusAsync`

Do **not** delete `_frontend.CaptureStatusAsync()` or the window-level status refresh mechanism merely because the Status page disappears.

The flow remains conceptually:

```text
frontend invalidation / window activation / setup completion
→ CaptureStatusAsync()
→ render the pieces that still have real user owners
→ evaluate prerequisite setup prompt
```

### 5.4 Distribute the snapshot

Change `RenderSystemStatus(FrontendStatusSnapshot snapshot)` so the snapshot is rendered into the new owner pages rather than into `StatusContent`.

Expected shape:

```csharp
DeviceContent.RenderDeviceSummary(snapshot);
SettingsContent.RenderRequiredComponents(snapshot);
```

Exact method names may differ, but keep the ownership narrow and obvious.

Do not make each page independently call `CaptureStatusAsync()` if the existing single MainWindow status capture already supplies the same snapshot.

Avoid duplicate polling/capture paths.

### 5.5 Manual Status Refresh button

Do not recreate the old Status Refresh button elsewhere.

The current event-driven/status-refresh paths are sufficient for this PR:

- frontend invalidation;
- initial/window activation refresh;
- prerequisite setup completion refresh.

If a later real support case requires a manual component refresh, design it separately.

### 5.6 Prerequisite setup must still work

The following window-level behavior must remain functional after Status is removed:

```text
CanInstallRequiredComponents
→ setup prompt
→ elevated prerequisite setup
→ result handling
→ optional reboot-required dialog
→ status refresh
```

`UpdatePrerequisiteSetupBusyUi()` must no longer call `StatusContent.SetRefreshing(...)` because no Status page exists.

Do not weaken the setup busy overlay or navigation input lock.

### 5.7 Update Status-specific user text

Any user-visible text that says:

```text
Check Status ...
```

must be updated because Status no longer exists.

For the current prerequisite setup failure dialog, use wording equivalent to:

```text
Check Settings > Required Components or the application log for details.
```

Do not leave a broken navigation reference in an error message.

---

## 6. Device Page — New Ownership

Target order:

```text
Device

[Device identity / support summary]
[MSI Center M]
[TDP Control]
[CPU Boost]
[Windows Power Mode]
```

No future placeholder cards.

### 6.1 Move device identity/support from Status

Move the useful device summary to the top of Device.

Preserve the existing information content:

```text
Manufacturer
Model
Supported/Unsupported presentation
Board
GPU model(s)
```

Keep it compact.

Do not rebuild a dashboard/hero layout.

Reuse the existing reliable presentation logic, including manufacturer normalization/support formatting, instead of duplicating a new formatter in Device.

A narrow Device method may accept the existing `FrontendStatusSnapshot` and render only `snapshot.Device` / `snapshot.Hardware` fields.

### 6.2 Move MSI Center M authority card from Controller to Device

Move the existing controller-authority UI and code with behavior unchanged.

The Device page becomes the UI owner for:

```text
FrontendCenterMStartupSnapshot
CaptureCenterMStartupAsync()
RequestCenterMAuthorityTransitionAsync(...)
CenterMStartupInfoBar
CenterMStartupEnableButton
CenterMStartupDisableButton
CenterMStartupPresentation
```

Preserve the existing important semantics:

- explicit Enable / Disable buttons;
- no inverted toggle;
- authoritative Windows state is the source of truth;
- Partial state can be repaired;
- confirmation occurs before backend mutation;
- transition is reboot-bound;
- no deferred `Restart Later` state;
- no UI-owned restart command;
- failed/cancelled Disable may re-expose `Enable and Restart` when the backend says that cleanup path is required;
- backend failure message has precedence;
- Runtime interruption is surfaced visibly;
- non-applicable/unavailable behavior stays conservative.

This is a **move**, not a redesign.

### 6.3 Refresh ownership

`DevicePage.Activate()` already refreshes device-level controls.

Extend Device refresh/activation using the smallest implementation so Center M state is also re-read when Device is entered.

Do not add another activation manager.

Acceptable shapes include:

```text
DevicePage.RefreshAsync()
  ├ CaptureCenterMStartupAsync
  ├ CaptureCpuBoostAsync
  ├ CaptureTdpAsync
  └ CapturePowerModeAsync
```

or one separate private Center M refresh called from the existing Device activation path.

Preserve the existing `StateInvalidated` subscription behavior for TDP/CPU Boost/Power Mode.

Remember the existing reason for explicit page-entry Center M refresh: the reboot-bound authority transition may not produce the same `StateInvalidated` signal as ordinary feature changes.

### 6.4 Ordering

The Center M card must appear above TDP Control.

The device summary must appear above Center M.

---

## 7. Controller Page — Button/Controller Behavior Only

After moving Center M authority out, the Controller page should contain the currently implemented controller behavior UI only.

Near-term visible content:

```text
Controller
[OEM1 / Center M Button mapping]
```

The existing OEM1 mapping persistence/order owner in `MainWindow` must remain unchanged.

Do not disturb:

```text
_oem1UiMapping
_oem1PersistedMapping
_oem1SaveChain
_oem1EditVersion
QueueOem1Mutation(...)
ControllerContent.MappingEditRequested
```

Those exist to serialize edits from the controller mapping surfaces and are unrelated to this navigation cleanup.

### Do not add placeholders

Do not add empty/disabled/Coming Soon cards for:

```text
WING
M1
M2
Joystick LED
Vibration Strength
```

Their placement is already documented in `docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md`.

They will be added only when implemented.

### Controller subtitle

It is acceptable to update the generic subtitle from:

```text
Configure controller settings.
```

to wording that better matches the new page role, e.g.:

```text
Configure controller buttons and controller-specific settings.
```

Do not invent functionality in the subtitle.

### Deferred wording cleanup

The current `CenterMButtonPage` still contains legacy text referring to:

```text
when Steam Input Routing is inactive
```

The broader stale-routing wording/source cleanup is PR-B unless changing the text is required to keep this PR internally coherent.

Do not expand PR-A into a new WING/OEM1 behavior-policy redesign.

---

## 8. Settings Page — Required Components + Developer Menu

Target visible structure:

```text
Settings

[Required Components]
  HidHide
  usbip-win2
  VIIPER

[Developer Menu]
```

### 8.1 Remove Launch at Windows startup UI

Remove the complete visible startup preference surface:

```text
LaunchAtStartupCard
LaunchAtWindowsStartupToggleSwitch
```

and the Settings-page-only event/render state that exists solely to drive that UI.

This includes page-local fields/handlers such as the current:

```text
_isLoadingStartupSettings
_lastKnownLaunchAtWindowsStartup
RenderLaunchAtStartup(...)
LaunchAtWindowsStartupToggleSwitch_Toggled(...)
SetLaunchAtWindowsStartupToggle(...)
```

Do not replace the card with a disabled card that says startup is mandatory.

### 8.2 Important scope boundary: backend startup contract stays for PR-B

This PR removes the **user-facing preference UI**.

Do not delete or redesign the Runtime/contract/startup-task authority APIs solely because the Settings toggle is gone.

In particular, do not use this PR to rewrite:

- Full1902 mandatory startup-task establishment;
- first-registration elevation path;
- Task Scheduler verification;
- controller-authority transition startup preparation;
- Runtime startup semantics;
- frontend transport protocol merely to remove an unused UI control.

The later legacy-code cleanup PR can classify which obsolete preference-facing contract pieces are safe to delete without harming mandatory Full1902 startup authority.

### 8.3 Required Components UI

Move the user-readable component state from Status to Settings.

Initial rows:

```text
HidHide
usbip-win2
VIIPER
```

Use the current frontend status snapshot fields:

```text
snapshot.Prerequisites.HidHideStatus / HidHideReason
snapshot.Prerequisites.UsbIpStatus / UsbIpReason
snapshot.Prerequisites.ViiperStatus / ViiperReason
```

The component UI is read-only.

Do not add repair/install buttons to this section.

Do not create another component state model if the existing `FrontendStatusSnapshot` already provides the required truth.

The existing `StatusPage.RenderGroup(...)` shape may be moved/refactored into Settings if useful, but avoid introducing a general-purpose dashboard/card framework for three rows.

### 8.4 Developer Menu stays in Settings

Preserve:

```text
DeveloperMenuEnabled gating
DeveloperMenuRequested event
warning dialog / Don't show this warning again behavior in MainWindow
Developer -> Sensor/Fan/Vibration child navigation hierarchy
```

Cleanup J's static Vibration Test shell remains static/unavailable.

Do not expose Developer Menu to normal users when the existing setting disables it.

### 8.5 Settings activation

The current `SettingsPage.ActivateAsync()` exists primarily to re-read launch-at-startup state after reboot-bound Center M transitions.

Once the startup toggle is removed, do not retain an otherwise pointless bootstrap refresh solely because the old architecture had it.

If no remaining Settings behavior requires page-entry bootstrap refresh, remove that page-entry refresh and the matching `MainWindow.ShowPage` call.

Required Components are already refreshed through MainWindow's authoritative status refresh path.

Developer Menu visibility may continue to come from the startup bootstrap unless a current supported same-process mutation requires otherwise.

Prefer the simpler current-product behavior over keeping a legacy refresh path with no remaining purpose.

---

## 9. Profile Page

Do not redesign Profile in this PR.

Keep current game catalog/detail behavior and feature order.

No new current-game Status row is required.

The existing Status-page `Steam Game` indicator may simply disappear.

Do not add a duplicate Steam status banner solely for feature parity.

---

## 10. How to Use

Keep the page and behavior unchanged.

Only move its NavigationView position to after Profile.

---

## 11. MainWindow Wiring Requirements

Expected high-level constructor/wiring changes:

### Remove

```text
StatusContent.RefreshRequested subscription
StatusNavigationItem initial selection
StatusContent page visibility switching
Status page manual-refresh UI dependency
```

### Change

```text
MainNavigationView.SelectedItem = DeviceNavigationItem
```

### Preserve

```text
_frontend.StateInvalidated += OnFrontendStateInvalidated
RefreshSystemStatusAsync()
prerequisite setup prompt path
OEM1 ordered mutation owner
Developer Menu navigation/warning behavior
sensor/fan probe navigation
static unavailable Vibration Test developer destination
Device/Profile Activate/Deactivate lifecycles
```

### New status rendering destinations

`RenderSystemStatus(...)` should update:

```text
Device summary
Settings Required Components
```

before/alongside existing prerequisite setup prompt evaluation.

Do not make the page that happens to be visible the authority for whether the latest snapshot is retained/rendered.

---

## 12. Do Not Overengineer This UI Migration

Do not add:

```text
NavigationService
PageRegistry
PageDescriptor abstraction
StatusStore
UI status authority
RequiredComponentManager
DeviceSummaryViewModel framework
new DI layer
new event bus
new persisted navigation state
new retry/state machine for page rendering
```

The existing shell already has one navigation owner (`MainNavigationState` + `MainWindow`) and one authoritative frontend status capture path.

Use those.

The goal is fewer ambiguous owners, not a new UI architecture framework.

---

## 13. Explicit Non-Goals

This PR must not change:

- Center M Enabled/Disabled backend authority semantics;
- PID1901/PID1902 switching;
- reboot sequencing;
- mandatory Addon Runtime lifetime;
- Task Scheduler establishment/verification;
- HidHide deterministic ownership/reconciliation;
- DirectInput ownership/recovery;
- physical PnP re-enumeration recovery;
- VIIPER attach/teardown;
- Steam/BPM detection;
- Xbox360/SteamDeck presentation switching;
- Win+G suppression;
- OEM1 low-level suppression/routing policy;
- M1/M2 implementation;
- WING mapping implementation;
- fan control implementation;
- battery limit implementation;
- joystick LED implementation;
- vibration-strength implementation;
- Profile persistence architecture;
- Overlay UI architecture/protocol.

No frontend transport protocol bump should be required for this PR.

---

## 14. Files Expected to Change

Exact diff may vary, but the primary implementation should be concentrated in:

```text
src/SteamInputAddonforClaw.UI/MainWindow.xaml
src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs
src/SteamInputAddonforClaw.UI/MainNavigationState.cs

src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml
src/SteamInputAddonforClaw.UI/Views/DevicePage.xaml.cs

src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml
src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs

src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml
src/SteamInputAddonforClaw.UI/Views/SettingsPage.xaml.cs

tests/SteamInputAddonforClaw.Tests/MainNavigationStateTests.cs
tests/SteamInputAddonforClaw.Tests/CenterMStartupPresentationTests.cs
tests/SteamInputAddonforClaw.Tests/UiArchitectureTests.cs
```

Possible small supporting changes:

```text
StatusPresentation.cs / tests
```

only if needed to reuse existing device display formatting cleanly.

### Intentionally allowed to remain until PR-B

```text
Views/StatusPage.xaml
Views/StatusPage.xaml.cs
obsolete backend/frontend launch-at-startup preference plumbing not required to compile this UI PR
other dead Status-only helpers/tests after visible ownership is removed
legacy routing-era wording cleanup not required for this structural move
```

Do not force unrelated cleanup into PR-A just because a symbol becomes unused.

---

## 15. Test Requirements

Update existing tests to assert the new product structure instead of weakening/removing architecture coverage.

### 15.1 Navigation tests

Add/adjust tests proving:

```text
new MainNavigationState().CurrentPage == Device
"Device"      -> Device
"Controller"  -> Controller
"Profile"     -> Profile
"HowToUse"    -> HowToUse
Settings       -> Settings
unknown/null top-level tag -> safe target (Device if using the recommended fallback)
```

Preserve Developer child-page round trips:

```text
Settings
→ Developer
→ Sensor/Fan/Vibration child
→ Developer
→ Settings
```

and existing mouse-back hierarchy semantics.

### 15.2 Main shell architecture

`UiArchitectureTests` should assert:

- no Status NavigationViewItem;
- no `StatusContent` instance in `MainWindow.xaml`;
- top-level menu order is `Device < Controller < Profile < HowToUse`;
- Device is the selected/default page in MainWindow/navigation state;
- Settings remains standard NavigationView Settings;
- Developer page remains a child of Settings;
- Developer vibration destination remains the Cleanup-J static unavailable shell.

Do not assert deletion of `StatusPage.xaml/.cs` in PR-A if those files are intentionally left for PR-B.

### 15.3 Device ownership tests

Update the current Center M architecture test so it proves:

```text
DevicePage contains CenterMStartupCard
ControllerPage does not contain CenterMStartupCard
Device identity summary exists above Center M
Center M exists above TDP Control
```

Preserve existing Center M behavior assertions:

- explicit Enable/Disable buttons;
- no routing master switch;
- entry refresh exists under Device ownership;
- no `Restart Later`;
- no UI `shutdown.exe`;
- no sticky `_centerMRestartRequired`;
- failed/cancelled Disable cleanup path remains exposed.

Move `CenterMStartupPresentationTests` from `ControllerPage.CenterMStartupPresentation` to the new Device owner rather than deleting those tests.

### 15.4 Settings tests

Replace the current startup-toggle/page-entry-refresh architecture assertion with tests proving:

```text
Required Components exists
HidHide row/status exists
usbip-win2 row/status exists
VIIPER row/status exists
Developer Menu remains
Launch at Windows startup card/toggle is absent
```

Also assert that Settings component rendering consumes the existing frontend prerequisite snapshot rather than creating fake local state.

### 15.5 Status refresh regression test / structural guard

Add a focused test/structural assertion proving that removing Status UI did **not** remove the frontend status capture path required by prerequisite setup.

At minimum prove current MainWindow still contains the equivalent of:

```text
CaptureStatusAsync
CanInstallRequiredComponents
PromptForPrerequisiteSetupAsync
```

and routes the snapshot to Device/Settings.

### 15.6 Existing feature tests

Do not weaken/remove unrelated tests for:

- TDP;
- CPU Boost;
- Power Mode;
- Profile;
- OEM1 ordered persistence;
- prerequisite setup;
- Full1902 lifecycle;
- HidHide;
- DirectInput;
- VIIPER;
- power transitions.

---

## 16. Manual UI Validation

Run the application and verify at minimum:

### Startup

```text
App opens on Device
No Status tab exists
Navigation order:
Device -> Controller -> Profile -> How to Use
Settings remains in footer/gear location
```

### Device

Verify:

```text
Device identity/support summary is visible at top
MSI Center M authority card is below identity
TDP follows Center M
CPU Boost follows TDP
Windows Power Mode follows CPU Boost
```

Exercise Center M card far enough to verify current state renders and the confirmation dialog opens, but do not perform an unnecessary reboot during ordinary desktop UI validation unless hardware validation specifically requires it.

### Controller

Verify:

```text
MSI Center M authority card is gone
existing OEM1/Center M button mapping remains usable
no fake WING/M1/M2/LED/Vibration cards appear
```

### Settings

Verify:

```text
Launch at Windows startup is gone
Required Components displays HidHide / usbip-win2 / VIIPER
Developer Menu remains visible only when developer mode is enabled
Developer navigation/back behavior still works
```

### Prerequisite state

When a test environment exposes setup-required state, verify the setup prompt/overlay still works without a Status page.

### Touch/layout

Do not introduce fixed layouts that regress existing WinUI touch use or the supported handheld window sizing.

---

## 17. Build / Verification

Before opening the PR:

1. Build Debug with zero errors.
2. Build Release with zero errors.
3. Run the full Release test suite.
4. No new skipped tests.
5. No frontend transport protocol bump unless an unexpected real contract change is proven necessary; if one appears necessary, stop and reassess scope first.
6. Review the final diff for accidental Runtime/controller-lifecycle changes.

Baseline immediately before this work order:

```text
main: 32fa738be413a79c41d7be002adf83a67930add3
Full Release suite reported by Cleanup J: 2317 passed, 0 failed, 0 skipped
```

The exact test count may change because architecture tests are updated, but the suite must finish green.

---

## 18. Acceptance Criteria

This PR is complete when all of the following are true:

- [ ] normal navigation order is `Device -> Controller -> Profile -> How to Use`;
- [ ] Settings remains the standard footer/gear destination;
- [ ] Status is not a visible navigation destination;
- [ ] Device is the default/initial page;
- [ ] `MainNavigationPage.Status` is no longer a valid navigation state;
- [ ] device identity/support information is visible at the top of Device;
- [ ] MSI Center M authority control lives on Device, not Controller;
- [ ] existing Center M reboot-bound behavior is preserved;
- [ ] TDP / CPU Boost / Windows Power Mode remain functional on Device;
- [ ] Controller retains currently implemented OEM1 mapping behavior;
- [ ] no future-feature placeholder cards were added;
- [ ] Settings contains read-only HidHide / usbip-win2 / VIIPER component status;
- [ ] Settings still contains the conditional Developer Menu entry;
- [ ] `Launch at Windows startup` is no longer user-visible;
- [ ] prerequisite setup prompt/overlay remains functional without Status UI;
- [ ] old `Check Status` user-facing text is gone;
- [ ] Profile and How to Use behavior are unchanged except navigation order;
- [ ] Cleanup J vibration transport remains removed;
- [ ] no Full1902 authority/lifecycle behavior changed;
- [ ] Debug/Release builds pass;
- [ ] full Release test suite passes.

---

## 19. Follow-Up PR-B Boundary

After this structural PR is merged and stable, PR-B may perform the deeper legacy cleanup, including classification/removal of code that is now truly unused.

Expected PR-B candidates include:

```text
StatusPage.xaml / StatusPage.xaml.cs deletion
Status-only presentation/helper/test cleanup that no remaining page uses
obsolete launch-at-startup preference-facing frontend/settings plumbing
stale Full1902-predecessor UI comments/names
legacy routing-era wording such as "when Steam Input Routing is inactive"
unused resources/event hooks left by the old page arrangement
```

PR-B must still preserve the actual mandatory Full1902 startup-task authority.

Do not preemptively delete lifecycle infrastructure in PR-A merely because its old optional UI toggle is gone.

---

## 20. Implementation Principle

The desired ownership after PR-A is simple:

```text
MainWindow
  = navigation + shared frontend status capture + window-level prerequisite UX

Device
  = device identity + Center M authority + global handheld controls

Controller
  = controller/button behavior

Profile
  = per-game overrides

Settings
  = user-readable required-component diagnostics + Developer entry
```

Use the existing owners and frontend contracts.

Do not solve a UI page move by introducing another authority layer.
