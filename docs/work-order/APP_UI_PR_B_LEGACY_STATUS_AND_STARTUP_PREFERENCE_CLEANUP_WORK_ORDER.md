# Work Order — App UI PR-B: Legacy Status and Startup Preference Cleanup

> **Date:** 2026-09-04  
> **Status:** Ready for implementation  
> **Reviewed baseline:** `main` at `94de6a968c56ad4111b03f93ddf6af860bf68584` (`App UI PR-A`, PR #486)  
> **Design authority:** `docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md`

---

## 1. Goal

Finish the source/contract cleanup intentionally deferred by App UI PR-A.

PR-A already changed the visible application structure to:

```text
Device
Controller
Profile
How to Use

----------------
Settings
```

and already removed the visible Status destination and the user-facing `Launch at Windows startup` card.

PR-B now removes the obsolete implementation residue that no longer has a product owner:

1. delete the disconnected `StatusPage` source and publish-asset expectations;
2. remove dead Status-only presentation/view-model code while preserving the still-used status snapshot pipeline;
3. remove `LaunchAtWindowsStartup` as a persisted/user/frontend preference end-to-end;
4. keep one internal Windows startup-registration owner as application infrastructure;
5. preserve the Full1902 safety rule that Addon controller authority cannot be entered unless background startup is verified;
6. preserve uninstall as the one normal path that removes the Addon startup task;
7. remove stale user-facing `Steam Input Routing` wording from the current OEM1 mapping page;
8. update tests and transport protocol honestly, without compatibility shims because this project is pre-release.

This is a cleanup/simplification PR.

Do **not** redesign controller authority, PID1901/PID1902 ownership, HidHide, DirectInput, VIIPER, WING/OEM1 low-level behavior, Steam/BPM detection, profile behavior, or Overlay architecture.

---

## 2. Product Policy for Windows Startup After This PR

The removed Settings toggle must not be replaced by a second hidden preference.

The target model is:

```text
Installed Addon
→ background startup registration is application infrastructure
→ Runtime startup repairs/verifies the owned task

Center M Disabled / Addon controller authority
→ startup registration MUST be proven before Center M can be disabled

Center M Enabled / MSI controller authority
→ the app may still run for Device / Profile / Overlay / diagnostics
→ do not implicitly remove background startup merely because controller authority is MSI

Uninstall preparation
→ after stock authority is independently proven
→ remove the Addon startup task
```

This preserves the current default application behavior while deleting the obsolete user preference.

### Important distinction

Do not reinterpret this cleanup as:

```text
Center M Enabled
→ automatically disable Addon startup
```

That would be a new product behavior change and could break non-controller background functionality such as Profile/device features.

Likewise, do not add a new persisted setting such as:

```text
BackgroundRuntimeEnabled
StartupMode
ControllerAuthorityStartupPolicy
```

There should be no replacement preference/state.

---

## 3. Current Source State Verified on `main`

This work order was written against `94de6a968c56ad4111b03f93ddf6af860bf68584`, after PR #486 was squash-merged.

### 3.1 Status is already gone from the visible shell

PR-A already removed:

```text
Status NavigationViewItem
MainNavigationPage.Status
StatusContent from MainWindow
```

and made Device the default page.

However these source files still exist:

```text
src/SteamInputAddonforClaw.UI/Views/StatusPage.xaml
src/SteamInputAddonforClaw.UI/Views/StatusPage.xaml.cs
```

They are now disconnected legacy source.

### 3.2 Publish verification still expects the Status XBF

The publish verifier still contains:

```text
ui\Views\StatusPage.xbf
```

in:

```text
scripts/verify-publish-assets.ps1
scripts/tests/verify-publish-assets.tests.ps1
```

Deleting the XAML page without updating these guards will break publish verification.

### 3.3 `StatusPresentation` is only partially useful now

Current:

```text
src/SteamInputAddonforClaw.UI/StatusPresentation.cs
```

contains:

```text
FormatManufacturerForDisplay(...)
FormatDeviceCompatibility(...)
FormatSteamGame(...)
IsWarning(...)
```

After PR-A:

```text
DevicePage.RenderDeviceSummary(...)
```

still needs only the manufacturer and device-compatibility formatting.

The old Status-page Steam row and warning InfoBar no longer exist, so:

```text
FormatSteamGame(...)
IsWarning(...)
```

have no normal UI owner and their Status-specific tests are now legacy coverage.

### 3.4 `StatusCardViewModel` is legacy naming

Current:

```text
src/SteamInputAddonforClaw.UI/StatusCardViewModel.cs
```

was originally a Status-page helper.

PR-A reused it only to build the three read-only Settings component rows.

Do not keep a Status-specific view model solely for three local Settings rows.

### 3.5 The status snapshot pipeline is still live and required

The following remains real production functionality and must **not** be deleted:

```text
IAddonFrontendControl.CaptureStatusAsync()
FrontendStatusSnapshot
FrontendPrerequisiteSnapshot
MainWindow.RefreshSystemStatusAsync()
MainWindow.RenderSystemStatus(...)
StateInvalidated-driven refresh
window-activation refresh
prerequisite setup prompt
prerequisite setup result refresh
DeviceContent.RenderDeviceSummary(...)
SettingsContent.RenderRequiredComponents(...)
```

`Status page removed` does **not** mean `status snapshot removed`.

The snapshot is now shared product state used by Device, Settings, and prerequisite setup.

### 3.6 `LaunchAtWindowsStartup` still exists as a full preference contract

Even though PR-A removed its UI, current backend/contract code still contains the old preference shape.

`AppSettings` still has:

```csharp
bool LaunchAtWindowsStartup = true
```

`SettingsStore` still:

```text
reads LaunchAtWindowsStartup from JSON
writes LaunchAtWindowsStartup to JSON
logs LaunchAtWindowsStartup
```

`StartupSettingsCoordinator` still has:

```text
_isLaunchAtWindowsStartupRequired
LaunchAtWindowsStartupRequiredMessage
IsLaunchAtWindowsStartupRequired
ChangeLaunchAtWindowsStartup(bool)
Repair() driven by saved preference + mandatory predicate
```

The frontend still carries:

```text
FrontendSettingsSnapshot.LaunchAtWindowsStartup
FrontendSettingsSnapshot.LaunchAtWindowsStartupRequired
FrontendBootstrapSnapshot.StartupRegistrationMessage
FrontendLaunchAtStartupResult
IAddonFrontendControl.SetLaunchAtWindowsStartupAsync(...)
```

The named-pipe transport still carries:

```text
FrontendRpcMethod.SetLaunchAtWindowsStartup
SetLaunchAtWindowsStartupRequest
client method
server dispatch
```

and frontend tests still validate that obsolete RPC.

### 3.7 Startup registration is also a real lifecycle dependency

Do not delete the underlying startup-registration owner.

Current Full1902 authority entry does this before Center M is disabled:

```text
CenterMRebootAuthorityTransition.DisableAsync
→ _startupSettings.ChangeLaunchAtWindowsStartup(true)
→ verify success
→ only then continue HidHide baseline + Center M disable
```

Current uninstall stock-restoration composition supplies:

```text
() => composition.StartupSettings.ChangeLaunchAtWindowsStartup(false)
```

as the final startup-task removal operation after stock authority is proven.

Those are real lifecycle responsibilities.

The preference-shaped API should be removed, but the lifecycle operations must remain.

### 3.8 Current frontend protocol is v23

Current desktop frontend transport protocol:

```text
FrontendTransportProtocol.CurrentVersion = 23
```

PR-B removes an RPC and required frontend fields, so the protocol must change once.

### 3.9 OEM1 page still contains obsolete routing wording

Current:

```text
src/SteamInputAddonforClaw.UI/Views/CenterMButtonPage.xaml
```

still says:

```text
Remapping is always enabled. Configure the normal action used when Steam Input Routing is inactive.
```

Full1902 no longer has a user-configurable Steam Input Routing mode.

The current physical/presentation product model is:

```text
Steam/BPM inactive → Xbox360 presentation
Steam/BPM active   → SteamDeck presentation
```

The UI wording must describe the current presentation semantics rather than a removed routing preference.

---

## 4. Delete the Disconnected Status Page

Delete:

```text
src/SteamInputAddonforClaw.UI/Views/StatusPage.xaml
src/SteamInputAddonforClaw.UI/Views/StatusPage.xaml.cs
```

Do not replace them with another dashboard/home page.

### Publish verifier cleanup

Remove the Status-page XBF requirement from:

```text
scripts/verify-publish-assets.ps1
scripts/tests/verify-publish-assets.tests.ps1
```

Do not weaken the rest of the publish-layout assertions.

The verifier should still require every currently shipping UI XBF that actually exists.

---

## 5. Reduce Status-Specific UI Helpers to Their Real Owners

### 5.1 Replace `StatusPresentation`

Do not keep a class whose documentation says it formats the removed Status page.

Preferred target:

```text
StatusPresentation.cs
→ DeviceSummaryPresentation.cs
```

with only the two still-used pure functions:

```text
FormatManufacturerForDisplay(...)
FormatDeviceCompatibility(...)
```

Update `DevicePage.RenderDeviceSummary(...)` to use the renamed helper.

Delete:

```text
FormatSteamGame(...)
IsWarning(...)
```

unless a current non-test production caller is found during implementation.

Before deletion, run a repository-wide reference audit.

### 5.2 Replace `StatusPresentationTests`

Rename/reduce the test suite to the still-supported behavior, for example:

```text
tests/SteamInputAddonforClaw.Tests/DeviceSummaryPresentationTests.cs
```

Keep manufacturer alias normalization and compatibility formatting coverage.

Delete tests for removed Status-page Steam/warning presentation.

Do not keep tests whose only purpose is to preserve dead UI behavior.

### 5.3 Remove `StatusCardViewModel`

Delete:

```text
src/SteamInputAddonforClaw.UI/StatusCardViewModel.cs
```

`SettingsPage.RenderRequiredComponents(...)` only needs three local rows.

Use the smallest local shape, e.g. tuples or a tiny method-local record/array.

Do **not** introduce a new shared `ComponentViewModel`, `DiagnosticCardModel`, manager, or abstraction merely to replace one obsolete three-field record.

---

## 6. Remove `LaunchAtWindowsStartup` From Persisted App Settings

### 6.1 `AppSettings`

Remove the positional member:

```csharp
bool LaunchAtWindowsStartup = true
```

The settings record should continue to own only real user/application preferences such as:

```text
LogLevel
SuppressDeveloperMenuWarning
DeveloperMenuEnabled
OEM1 mapping
WING mapping
Overlay tab order
```

Update positional construction sites directly.

Do not add a compatibility constructor overload.

### 6.2 `SettingsStore`

Remove:

```text
LaunchAtWindowsStartup JSON read
LaunchAtWindowsStartup JSON write
LaunchAtWindowsStartup log fields
```

An old pre-release JSON file containing the property may simply have that unknown property ignored.

Do not add:

```text
settings schema migration
legacy startup preference migration
one-time conversion flag
```

This is a pre-release cleanup.

Add/update a test proving that an old JSON object containing `LaunchAtWindowsStartup` does not break loading and that the next save no longer serializes the property.

Do not reset unrelated settings merely because the old property exists.

---

## 7. Simplify `StartupSettingsCoordinator` Into Lifecycle Operations

Keep `StartupSettingsCoordinator` as the existing owner rather than adding a second startup-registration service.

Remove preference-specific state/API:

```text
_isLaunchAtWindowsStartupRequired
LaunchAtWindowsStartupRequiredMessage
IsLaunchAtWindowsStartupRequired
ChangeLaunchAtWindowsStartup(bool)
```

### Required narrow operations

Expose explicit lifecycle operations instead.

Recommended shape:

```csharp
public StartupRegistrationResult EnsureStartupRegistration()
    => _startupManager.Synchronize(true);

public StartupRegistrationResult RemoveStartupRegistrationForUninstall()
    => _startupManager.Synchronize(false);
```

Exact names may vary, but the meaning must be explicit.

The removal method should be named narrowly enough that ordinary feature code does not look like it can toggle application startup as a preference.

### Runtime startup

Replace preference-driven `Repair()` behavior with unconditional installed-app startup repair/verification.

Acceptable target:

```text
Runtime composition startup
→ EnsureStartupRegistration()
```

The already-running Runtime should retain current failure semantics: a registration repair failure is reported/logged but must not intentionally terminate a Runtime that is already running.

Do not add periodic Task Scheduler polling.

### Logging

Because `StartupRegistrationMessage` is being removed from the frontend bootstrap, ensure the startup registration result is still logged at Runtime composition/startup.

At minimum log:

```text
success/failure
message/reason
```

Do not create a new user-visible startup-status card in Settings.

---

## 8. Preserve Full1902 Authority Safety

### 8.1 Disable Center M

Replace the preference-shaped call:

```csharp
_startupSettings.ChangeLaunchAtWindowsStartup(true)
```

with the new ensure operation.

The ordered safety contract must remain exactly:

```text
read-only admission
→ verify/repair Addon startup registration
→ verify Disabled-mode HidHide baseline
→ disable + read-back Center M roots
→ restart
```

If startup registration cannot be established and verified:

```text
Center M must remain Enabled
```

Do not weaken this gate merely because startup is no longer a user preference.

### 8.2 Enable Center M

Do not add startup-task removal to `Enable Center M and Restart` in this PR.

MSI controller authority and Addon application startup are separate concerns now that the app also owns Device/Profile/Overlay functionality.

Preserve the current stock-authority restoration sequence.

### 8.3 Uninstall

The existing stock-safe uninstall path must remain the normal removal owner for the startup task.

Rewire its callback from:

```text
ChangeLaunchAtWindowsStartup(false)
```

to the new narrow uninstall removal method.

Preserve ordering:

```text
release physical ownership
→ independently prove PID1901 stock baseline
→ release Addon HidHide baseline
→ enable/read-back Center M roots
→ release stock-authority suppression state
→ remove Addon startup registration
```

No early startup-task removal.

---

## 9. Remove Startup Preference From Frontend Contracts

### 9.1 `FrontendSettingsSnapshot`

Remove:

```text
LaunchAtWindowsStartup
LaunchAtWindowsStartupRequired
```

Do not replace them with another startup/status bool.

Keep:

```text
LogLevel
SuppressDeveloperMenuWarning
Oem1Mapping
DeveloperMenuEnabled
WingMapping
```

and other current real members unchanged.

### 9.2 `FrontendBootstrapSnapshot`

Remove:

```text
StartupRegistrationMessage
```

No normal UI consumes it after PR-A.

Update all construction/deserialization/test sites directly.

### 9.3 Delete launch mutation result

Delete:

```text
FrontendLaunchAtStartupResult
```

### 9.4 `IAddonFrontendControl`

Delete:

```text
SetLaunchAtWindowsStartupAsync(...)
```

No default/no-op compatibility method.

---

## 10. Remove Startup Preference From In-Process Frontend Wiring

In:

```text
src/SteamInputAddonforClaw/Frontend/InProcessAddonFrontendControl.cs
```

remove:

```text
_registrationMessage
registrationMessage constructor parameter
SetLaunchAtWindowsStartupAsync(...)
LaunchAtWindowsStartup fields from MapSettings()
LaunchAtWindowsStartupRequired from MapSettings()
```

`MapSettings()` must continue projecting the real settings used by Settings/Controller/Developer UI.

Do not alter CPU/TDP/Profile/Center M/prerequisite behavior.

### Composition cleanup

Remove `StartupRegistrationMessage` from:

```text
AddonRuntimeComposition
AddonRuntimeCompositionFactory return value
AddonProcessHost → InProcessAddonFrontendControl construction
```

Keep the startup registration operation itself; only stop carrying its message as frontend bootstrap state.

---

## 11. Remove the Startup RPC From Named-Pipe Transport

Current desktop frontend protocol is v23.

This PR removes an RPC and required contract members.

### Protocol

Bump exactly once:

```text
23 → 24
```

Document v24 in the existing protocol history comment.

Suggested reason:

```text
Version 24 removes the obsolete LaunchAtWindowsStartup user-preference contract:
SetLaunchAtWindowsStartup RPC/request/result, FrontendSettingsSnapshot startup fields,
and FrontendBootstrapSnapshot.StartupRegistrationMessage.
```

Pre-release project:

- no v23 compatibility adapter;
- no ignored legacy RPC handler;
- no optional compatibility fields.

### Delete transport surface

Remove:

```text
FrontendRpcMethod.SetLaunchAtWindowsStartup
SetLaunchAtWindowsStartupRequest
NamedPipeAddonFrontendClient.SetLaunchAtWindowsStartupAsync(...)
NamedPipeAddonFrontendServer dispatch branch
```

Update fake frontend controls and malformed-request/round-trip tests accordingly.

---

## 12. Startup Tests — Replace Preference Coverage With Ownership Coverage

The following current suite is preference-shaped:

```text
tests/SteamInputAddonforClaw.Tests/StartupSettingsCoordinatorMandatoryTests.cs
```

Replace/refactor it around the actual lifecycle contract.

Recommended coverage:

### Installed-app startup ownership

```text
EnsureStartupRegistration
→ Synchronize(true)
→ success is returned
```

```text
EnsureStartupRegistration failure
→ failure is returned
→ no fake persisted preference is created
```

### Runtime startup

Prove Runtime composition/startup invokes startup ensure/repair as designed.

### Full1902 Disable transition

Existing Center M transition tests must continue proving:

```text
startup ensure succeeds
→ HidHide mutation may proceed
```

and:

```text
startup ensure fails
→ HidHide not mutated
→ Center M roots not disabled
→ restart not requested
```

### Uninstall

Keep/prove:

```text
startup removal occurs only after stock authority has been proven
```

and:

```text
startup removal failure
→ uninstall preparation fails closed
```

### Remove obsolete frontend suite

Delete or fully repurpose:

```text
tests/SteamInputAddonforClaw.Tests/MandatoryLaunchAtStartupFrontendTests.cs
```

There should be no frontend test for a startup toggle/mandatory flag after those fields no longer exist.

---

## 13. Frontend Contract and Transport Test Cleanup

Audit and update at least:

```text
tests/SteamInputAddonforClaw.Tests/FrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/FrontendNamedPipeTransportTests.cs
tests/SteamInputAddonforClaw.Tests/MandatoryLaunchAtStartupFrontendTests.cs
tests/SteamInputAddonforClaw.Tests/SettingsStoreTests.cs
tests/SteamInputAddonforClaw.Tests/StartupSettingsCoordinatorMandatoryTests.cs
tests/SteamInputAddonforClaw.Tests/CenterMRebootAuthorityTransitionTests.cs
tests/SteamInputAddonforClaw.Tests/UiArchitectureTests.cs
```

Also update any other compile-time construction sites found by repository-wide search.

### Required negative contract guard

Add a structural/contract regression assertion proving the removed API does not return, for example:

```text
FrontendRpcMethod has no SetLaunchAtWindowsStartup
IAddonFrontendControl source has no SetLaunchAtWindowsStartupAsync
FrontendSettingsSnapshot has no LaunchAtWindowsStartup member
FrontendBootstrapSnapshot has no StartupRegistrationMessage member
AppSettings has no LaunchAtWindowsStartup member
```

Use existing repository testing style; do not build reflection infrastructure solely for this.

---

## 14. Update OEM1 User-Facing Wording

Update the current `CenterMButtonPage.xaml` description.

Remove wording based on the deleted product model:

```text
when Steam Input Routing is inactive
```

Use current presentation terminology.

Recommended text:

```text
Configure the Center M button action used in Xbox360 presentation.
```

or an equally concise wording that means the same thing.

Do not change:

- gesture recognition;
- `Oem1MappingSettings` data shape;
- runtime slot resolution;
- action capabilities;
- suppression ownership;
- WING behavior;
- SteamDeck-presentation OEM1 policy.

### Local stale comments

While touching `CenterMButtonPage.xaml.cs`, remove/update comments that describe a UI with a global remapping switch and four visible editors if they no longer describe the current page.

Do not turn this into a broad OEM1 contract rename.

In particular, do not rename persisted/runtime enum members such as `RoutingSingle`/`RoutingDouble` in this PR solely for naming aesthetics.

---

## 15. Explicit Non-Goals

Do **not** implement or redesign:

- WING mapping policy;
- M1/M2 mapping;
- Joystick LED;
- Vibration Strength;
- Fan Control;
- Battery Charge Limit;
- new Settings cards;
- new Status/Home/Dashboard page;
- manual startup enable/disable UI;
- Task Scheduler monitoring loop;
- startup authority manager/state machine;
- new settings schema/version framework;
- Center M authority state persistence;
- PID1901/PID1902 lifecycle changes;
- HidHide baseline changes;
- VIIPER ownership changes;
- DirectInput recovery changes;
- sleep/hibernate/resume sequencing changes;
- Overlay protocol changes unless a compile dependency genuinely requires a corresponding constructor update (the desktop Frontend protocol v24 is the intended wire change here).

No future-feature placeholder cards.

---

## 16. Required Repository-Wide Reference Audit

Before finalizing, run searches equivalent to:

```text
rg -n "StatusPage|StatusCardViewModel|StatusPresentation" src tests scripts
rg -n "LaunchAtWindowsStartup|SetLaunchAtWindowsStartup|FrontendLaunchAtStartupResult|StartupRegistrationMessage|IsLaunchAtWindowsStartupRequired" src tests scripts
rg -n "Steam Input Routing is inactive" src tests
```

Expected final state:

### Allowed `Status` terminology

The following remain valid:

```text
FrontendStatusSnapshot
SystemStatusProvider
CaptureStatusAsync
RefreshSystemStatusAsync
status logs / prerequisite status
```

Do not delete or rename legitimate status-domain concepts merely to remove the old page.

### Forbidden obsolete UI/preference symbols

Normal production/test source should no longer contain:

```text
StatusPage
StatusCardViewModel
FrontendLaunchAtStartupResult
SetLaunchAtWindowsStartupAsync
FrontendRpcMethod.SetLaunchAtWindowsStartup
SetLaunchAtWindowsStartupRequest
LaunchAtWindowsStartupRequired
StartupRegistrationMessage
AppSettings.LaunchAtWindowsStartup
```

Historical docs/work-orders may retain old terminology as historical records.

Do not edit old work orders solely to erase historical names.

---

## 17. Validation

### Build

Run:

```text
dotnet build SteamInputAddonforClaw.sln -c Debug
dotnet build SteamInputAddonforClaw.sln -c Release
```

Expected:

```text
0 errors
0 warnings
```

### Full tests

Run the complete Release test suite.

No filtered-only acceptance.

### Publish verification

Run the repository's existing publish-layout / asset verification scripts, including the verifier whose StatusPage XBF expectation is removed.

Prove:

```text
StatusPage.xbf is no longer required or shipped as a page artifact
all remaining required UI assets are still verified
```

### Transport

Prove:

```text
desktop Frontend protocol = 24
v23 peer fails handshake as expected
SetLaunchAtWindowsStartup no longer exists on the wire
all remaining desktop frontend RPCs round-trip
```

### Settings persistence

Prove:

```text
new save contains no LaunchAtWindowsStartup
old pre-release JSON containing LaunchAtWindowsStartup still loads unrelated settings
next save drops the obsolete property
```

### Controller authority

Prove existing lifecycle tests remain green for:

```text
Disable Center M → startup task verified before authority commit
startup verification failure → fail closed before Center M disable
Enable Center M → stock restoration behavior unchanged
uninstall → startup task removed only after stock authority proof
```

### Manual UI smoke test when hardware is available

On a supported MSI Claw:

```text
1. Launch app
2. initial page is Device
3. no Status page exists
4. Device identity renders correctly
5. Center M authority card remains functional
6. Controller page still edits current OEM1 action
7. OEM1 description contains no "Steam Input Routing" wording
8. Settings shows Required Components + Developer Menu only
9. no startup toggle/card exists
10. restart Windows and confirm the installed app still starts in background
11. confirm Device/Profile features remain available with Center M Enabled
12. Disable Center M and Restart still proves startup before switching authority
```

Hardware-unavailable CI may mark only the physical smoke test blocked; automated validation must still be complete.

---

## 18. Completion Criteria

PR-B is complete only when all are true:

- `StatusPage.xaml/.cs` deleted;
- publish verifier no longer expects `StatusPage.xbf`;
- Status-only Steam/warning presentation helpers removed;
- Device summary formatting remains covered;
- `StatusCardViewModel` removed without replacement abstraction;
- `AppSettings` no longer contains `LaunchAtWindowsStartup`;
- `SettingsStore` no longer reads/writes it;
- no startup preference/mandatory flag is exposed to the frontend;
- no startup-registration message is carried in bootstrap;
- `SetLaunchAtWindowsStartup` RPC/result/request/client/server surface is gone;
- desktop frontend protocol is bumped exactly once to v24;
- one existing startup-registration owner remains;
- installed-app Runtime startup ensures/verifies startup registration;
- Disable Center M still fails closed if startup cannot be proven;
- Enable Center M behavior is otherwise unchanged;
- uninstall remains the explicit normal startup-task removal path;
- current Device/Profile/Overlay functionality is not gated by a removed preference;
- stale OEM1 `Steam Input Routing` wording is gone;
- no new manager/state machine/wrapper/persistence abstraction was introduced;
- Debug + Release builds pass cleanly;
- full Release test suite passes;
- publish verification passes.

---

## 19. Final Invariant

After PR-B:

> **The old Status page and Windows-startup preference no longer exist as product concepts. The application still uses one authoritative status snapshot pipeline for Device identity, Required Components, and prerequisite setup, and one existing startup-registration owner for installed-app lifecycle. Background startup is managed infrastructure rather than a user preference; Addon controller authority still cannot be entered until that startup registration is proven, while uninstall remains the normal path that removes it. No new authority, persisted state, or abstraction is introduced.**
