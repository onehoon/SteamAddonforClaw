# Work Order — Retire QamHost and Consolidate Addon Quick Settings on the WinUI3 Overlay

> **Date:** 2026-09-22  
> **Target repository:** `onehoon/SteamAddonforClaw`  
> **Source-review baseline:** `main` at `e242978a48bf751424b7f8bd3de4dba2c4b1a525`  
> **Implementation shape:** one focused retirement/cleanup PR  
> **Product direction:** the Addon-owned Quick Settings surface is the WinUI3 Overlay only. Steam's own native Quick Access Menu remains available through the existing Steam Deck Quick Access system-button pulse.

This work order is authoritative for the QamHost retirement. Read it together with the current documents under:

```text
docs/Full 1902 Implementation/
```

The Full1902 controller-ownership and lifecycle contracts remain unchanged.

---

## 1. Goal

Remove the Addon's custom Steam QAM integration completely:

```text
SteamInputAddonforClaw.QamHost
Steam GamepadUI / React patching
CEF DevTools / CDP attachment
Addon tab injection
Addon-first QAM tab-selection intent
QAM-specific frontend pipe/process lifecycle
```

and make the WinUI3 Overlay the only Addon-owned Quick Settings surface.

The final product split is:

```text
Main application UI
  -> WinUI3 Main UI

Addon Quick Settings
  -> WinUI3 Overlay
  -> FrontButtonAction.QuickSettingsOverlay

Steam native UI
  -> Steam Button
  -> Steam native Quick Access Menu
  -> driven only through the existing virtual Steam Deck system-button pulses
```

Do **not** remove Steam native Quick Access invocation.

---

# 2. Mandatory first step — create the final QamHost backup branch

Before deleting or changing any QAM/QamHost code, create one permanent remote backup branch from the **actual latest `origin/main` at implementation start**.

Required branch name:

```text
archive/qamhost-final-2026-09-22
```

The source-review baseline of this work order is:

```text
e242978a48bf751424b7f8bd3de4dba2c4b1a525
```

However, **do not blindly create the backup branch from that SHA**.

This work-order file itself is being committed after that review baseline, and `main` may receive additional commits before implementation begins. The backup must capture the real final pre-retirement tree.

Required sequence:

```bash
git fetch origin
git checkout main
git pull --ff-only origin main

BACKUP_BASE="$(git rev-parse HEAD)"

git ls-remote --exit-code --heads origin archive/qamhost-final-2026-09-22
```

If the backup branch does not already exist:

```bash
git branch archive/qamhost-final-2026-09-22 "$BACKUP_BASE"
git push origin archive/qamhost-final-2026-09-22
```

Then verify:

```bash
git rev-parse "$BACKUP_BASE"
git ls-remote --heads origin archive/qamhost-final-2026-09-22
```

The remote branch SHA must equal `BACKUP_BASE`.

Rules:

- never force-push this archive branch;
- never repoint it later;
- never create it from a feature branch;
- never begin QamHost deletion until the remote backup branch is verified;
- record the backup SHA in the implementation PR description.

If `archive/qamhost-final-2026-09-22` already exists, verify its SHA and stop if it does not represent the intended final pre-retirement main tree. Do not overwrite it.

After the backup is proven, create the implementation branch from the same latest main baseline.

---

# 3. Critical product boundary — QamHost is not Steam Quick Access

The existing production path already separates the Addon's custom QAM host from Steam's native QAM invocation.

The Steam native Quick Access path is conceptually:

```text
physical front button
  -> MsiClawFrontButtonRuntime
  -> FrontButtonActionExecutor
  -> FrontButtonAction.SteamQuickAccess
  -> TryRequestQuickAccessPulse()
  -> active SteamDeck presentation
  -> SteamDeckSystemButtonOverlay.RequestQuickAccessPulse()
  -> existing continuous SteamDeck publisher
  -> VIIPER SteamDeck state
  -> Steam receives Quick Access button
  -> Steam native QAM toggles
```

This path is the one that must survive.

Current code already describes the native Quick Access pulse as the sole open/close authority. QamHost only adds the optional pre-open Addon-tab-selection intent.

After this PR:

```text
SteamQuickAccess
  -> request native SteamDeck QuickAccess pulse directly
  -> Steam native QAM toggles
```

There is no Addon tab inside Steam QAM anymore.

---

# 4. Non-negotiable preservation scope

The following must remain intact.

## 4.1 Front-button action contract

Keep:

```text
FrontButtonAction.QuickSettingsOverlay
FrontButtonAction.SteamBigPicture
FrontButtonAction.SteamButton
FrontButtonAction.SteamQuickAccess
FrontButtonAction.KeyboardHotkey
FrontButtonAction.LaunchApplication
```

Do not remove `SteamQuickAccess` from:

```text
FrontButtonMappingSettings
FrontButtonActionCapabilities
Controller page UI
FrontButtonActionExecutor
MsiClawFrontButtonRuntime
```

The current frozen defaults remain:

```text
Normal.Gamebar = QuickSettingsOverlay
Normal.CenterM = SteamBigPicture

Steam.Gamebar = SteamButton
Steam.CenterM = SteamQuickAccess
```

No mapping migration is required.

## 4.2 Native Steam system-button implementation

Keep:

```text
SteamDeckSystemButtonOverlay
RequestSteamPulse()
RequestQuickAccessPulse()
IMsiClawAddonPresentation.TryRequestSteamPulse()
IMsiClawAddonPresentation.TryRequestQuickAccessPulse()
MsiClawAddonPresentation pulse eligibility/lifecycle
Canonical SteamDeck publication integration
```

Do not create a new Steam-QAM launcher.

Do not invoke Steam internals directly.

Do not replace the system-button pulse with keyboard synthesis, URI activation, CDP, React calls, window messages, or a new Steam API abstraction.

## 4.3 WinUI3 Overlay

Keep the existing Overlay architecture and lifecycle, including:

```text
SteamInputAddonforClaw.Overlay
OverlayProcessController
OverlayControllerInputRouter
Runtime-owned coordinated Overlay toggle
Main UI <-> Overlay visible-surface ordering
Overlay controller capture / neutral publication / release-to-resume behavior
Overlay typed Quick Settings transport
Overlay tab order
Overlay Device/Profile projections
Overlay ClawHUD integration
```

This PR is not an Overlay redesign.

## 4.4 Full1902 controller lifecycle

Do not change:

```text
PID1901 / PID1902 authority
HidHide ownership
DirectInput acquisition
Win+G suppression authority
VIIPER server/bus ownership
SteamDeck/Xbox360 attach/detach policy
presentation reconcile
sleep / hibernate / resume
PnP recovery
stock restoration
shutdown/restart authority
rumble
M1/M2 mapping
```

QamHost retirement is a UI/integration cleanup, not a controller architecture PR.

---

# 5. Delete the QamHost project completely

Delete the entire directory:

```text
src/SteamInputAddonforClaw.QamHost/
```

At the reviewed baseline this includes:

```text
CdpCommandCorrelator.cs
CdpEvaluateResult.cs
CdpTarget.cs
CdpTargetSnapshotFormatter.cs
DeterministicQamInstallException.cs
Frontend/qam.js
GamepadUiTargetSelector.cs
Program.cs
Properties/AssemblyInfo.cs
QamFrontendBridge.cs
QamHostGeometryDiagnostic.cs
QamHostLogger.cs
QamHostManagedLifetime.cs
QamHostRecovery.cs
QamHostTargetSelector.cs
QamHostTransformPatcher.cs
QuickAccessGeometryDiagnostic.cs
QuickAccessTargetSelector.cs
SteamGamepadUiCdpClient.cs
SteamInputAddonforClaw.QamHost.csproj
```

Do not retain a dormant project, build flag, feature flag, fallback executable, or disabled QAM injection path.

The archive branch is the recovery mechanism if this implementation ever needs to be studied again.

---

# 6. Remove QamHost process lifecycle from the Runtime

Delete:

```text
src/SteamInputAddonforClaw/Lifecycle/QamHostProcessController.cs
```

Remove all production wiring from `AddonProcessHost`, including the current concepts:

```text
_qamHostController
QamHostProcessController construction
OnActualRunningAppIdChanged -> QamHost forwarding
OnBigPictureStateChanged -> QamHost forwarding
BeginShutdown/DisposeAsync QamHost shutdown
bounded QamHost unexpected restart attempts
QAM.Host process logging
```

RunningAppID and BPM events must continue to drive the existing Full1902 Xbox360 <-> SteamDeck presentation reconcile exactly as before.

The removal must not alter that presentation decision.

---

# 7. Remove the QAM-specific frontend pipe and Addon-tab-selection protocol

The Runtime currently starts a dedicated QAM frontend server using:

```text
FrontendPipeEndpoint.CreateQamForCurrentUser()
```

and keeps:

```text
_qamFrontendServer
```

for QamHost.

Delete that dedicated pipe path.

Remove the QAM-only wire contract:

```text
FrontendPipeEndpoint.CreateQamForCurrentUser()

FrontendNotificationKind.SelectAddonOnNextQuickAccessOpenRequested

FrontendRpcMethod.AcknowledgeQamSelectAddonOnNextOpenPrepared

NamedPipeAddonFrontendServer.RequestSelectAddonOnNextQuickAccessOpenAsync(...)

ServedConnection.SendSelectAddonOnNextQuickAccessOpenRequestedAsync

ServedConnection.SelectAddonPreparedAcknowledgement

NamedPipeAddonFrontendClient.SelectAddonOnNextQuickAccessOpenRequested

NamedPipeAddonFrontendClient.AcknowledgeQamSelectAddonOnNextOpenPreparedAsync(...)
```

Remove matching codec/request/notification handling and tests.

Do **not** remove the generic/shared Quick Settings RPCs still used by WinUI3 surfaces, including:

```text
CaptureDeviceQuickSettings
CaptureQuickSettingsPage
CaptureAddonQuickSettingsShell
CaptureAddonQuickSettingsTabOrder
MoveAddonQuickSettingsTab
MutateQuickSetting
SetQuickSettingsCurrentPowerSourceOnly
```

Those contracts now serve the Main UI / Overlay product and are not QamHost code merely because QAM used them historically.

## 7.1 Protocol version

This is a breaking frontend wire-contract removal.

At the reviewed baseline:

```text
FrontendTransportProtocol.CurrentVersion = 39
```

Increment the protocol version exactly once from the actual current value at implementation time.

If no intervening protocol PR lands, this becomes:

```text
40
```

Update the version history comment to say that the QamHost-only pipe notification/ack contract was retired while the shared Overlay/Main-UI Quick Settings RPCs remain.

Pre-release policy remains:

```text
no compatibility shim
old peer -> handshake failure
```

---

# 8. Simplify Steam Quick Access to the native pulse only

Current `AddonProcessHost` has QAM-first coordination:

```text
RequestSteamQuickAccess()
  -> CoordinateSteamQuickAccessAsync()
  -> _quickAccessRequestGate
  -> QAM Addon-first intent
  -> wait for acknowledgement / timeout
  -> TryRequestQuickAccessPulse()
```

Delete the Addon-tab-selection coordination.

Remove:

```text
_quickAccessRequestGate
_quickAccessCoordination
CoordinateSteamQuickAccessAsync()
QAM Addon-first intent logging
QAM acknowledgement timeout
shutdown drain of the QAM coordination task
```

The host-facing action should become a narrow direct request to the existing presentation owner.

Conceptually:

```csharp
private bool RequestSteamQuickAccess()
{
    if (Volatile.Read(ref _processShutdownStarted) != 0)
        return false;

    return _presentationOwnership?.TryRequestQuickAccessPulse() == true;
}
```

Exact syntax may follow current code conventions.

Do not add:

- a new semaphore;
- a new scheduler;
- a new state machine;
- a new retry loop;
- a new QAM authority;
- a second system-button publisher.

The existing presentation owner already owns pulse eligibility, suspend state, active-presentation validation, and publication.

---

# 9. Retire Steam CEF remote-debugging setup safely

QamHost currently needs Steam CEF remote debugging and startup calls:

```text
SteamCefDebugBootstrap.Ensure()
```

which can create:

```text
<Steam install>/.cef-enable-remote-debugging
```

with Addon ownership evidence:

```text
SteamInputAddonforClaw-Data/steam-cef-marker.json
```

New builds no longer need that marker.

Therefore:

1. remove all production calls that **create or ensure** the CEF marker;
2. remove the QamHost CEF bootstrap behavior;
3. preserve one narrow legacy cleanup path long enough to remove markers this Addon previously created.

## 9.1 Ownership-safe legacy cleanup rule

Never delete a CEF marker merely because it exists.

Only delete it when the existing Addon ownership evidence proves this Addon created/owns it.

Required behavior:

```text
steam-cef-marker.json absent
  -> do nothing

valid Addon ownership evidence present
  -> resolve recorded Steam directory
  -> delete .cef-enable-remote-debugging if present
  -> delete ownership evidence
  -> success

cleanup fails / ownership evidence malformed
  -> log warning
  -> preserve ownership evidence
  -> retry on a later normal startup or uninstall
```

Do not touch a user-created or third-party-created marker when Addon ownership evidence is absent.

The existing `SteamCefDebugBootstrap.RemoveOwnedMarker()` semantics are the reference for ownership-safe deletion.

Implementation should remove the obsolete `Ensure` side entirely and keep only the smallest cleanup implementation needed.

A small clearly named legacy cleanup helper is acceptable if it makes the dead bootstrap code removable. Do not build a migration framework or generalized Steam settings manager.

## 9.2 Startup cleanup

Run the ownership-safe cleanup as best-effort startup cleanup.

Failure is feature-local and must not block:

```text
Full1902 Runtime startup
controller ownership
presentation startup
Main UI
Overlay
```

Uninstall must continue to invoke the same ownership-safe cleanup.

Keep `AddonDataPaths.CefMarkerOwnershipPath` while this compatibility cleanup exists.

---

# 10. Remove QAM from build/publish/package layout

Update:

```text
SteamInputAddonforClaw.slnx
scripts/publish-layout.ps1
scripts/verify-publish-assets.ps1
scripts/report-publish-size.ps1
scripts/tests/verify-publish-assets.tests.ps1
scripts/tests/report-publish-size.tests.ps1
```

Required final publish layout:

```text
Runtime
ui/
overlay/
fse/
required helpers/dependencies
```

There must be no:

```text
qam/
SteamInputAddonforClaw.QamHost.exe
Frontend/qam.js
QAM Host publish-size component
QAM-specific runtime-payload verification
```

Update publish log text so it no longer claims QAM is published.

Do not disturb:

```text
UI publish
Overlay publish
FSE Home distribution artifacts
TDP helper
VIIPER payload
ClawHUD lock/provisioning
framework-dependent Windows App SDK validation
```

---

# 11. Tests — delete retired tests, preserve native Quick Access tests

Delete QamHost-only test files:

```text
tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/QamHostCdpCommandCorrelatorTests.cs
tests/SteamInputAddonforClaw.Tests/QamHostCdpEvaluateResultTests.cs
tests/SteamInputAddonforClaw.Tests/QamHostGamepadUiTargetSelectorTests.cs
tests/SteamInputAddonforClaw.Tests/QamHostProcessControllerTests.cs
```

Also remove/update QAM-specific assertions in shared tests.

At minimum inspect:

```text
MsiClawFrontButtonRuntimeTests.cs
FrontendNamedPipeTransportTests.cs
OverlayTransportTests.cs
AddonQuickSettingsSurfaceParityTests.cs
SteamCefDebugBootstrapTests.cs
UninstallBootstrapTests.cs
```

## 11.1 Preserve these native-pulse tests

Do not delete or weaken tests that prove:

```text
SteamDeckSystemButtonOverlay.RequestQuickAccessPulse()
SteamDeckSystemButtonOverlay.Apply()
IMsiClawAddonPresentation.TryRequestQuickAccessPulse()
SteamDeck presentation accepts the pulse
Xbox360 presentation refuses the pulse
suspend-paused presentation refuses the pulse
pulse merges into the existing continuous publisher
```

Relevant current suites include:

```text
SteamDeckSystemButtonOverlayTests.cs
MsiClawAddonPresentationTests.cs
CanonicalSteamDeckInputPublisherTests.cs
FrontButtonDispatchTests.cs
MsiClawFrontButtonRuntimeTests.cs
```

## 11.2 Required regression coverage

Add/update tests proving:

1. `FrontButtonAction.SteamQuickAccess` still exists and remains Steam-domain capable.
2. the Controller page still labels it `Steam Quick Access`.
3. production front-button wiring still reaches `IMsiClawAddonPresentation.TryRequestQuickAccessPulse()`.
4. the QAMHost intent/acknowledgement is no longer required before the pulse.
5. no QAM-specific pipe is created.
6. Overlay transport remains available and unchanged in ownership.
7. legacy CEF cleanup deletes an Addon-owned marker.
8. legacy CEF cleanup does not delete an unowned marker.
9. cleanup failure preserves ownership evidence for retry.
10. publish verification succeeds with no `qam/` directory.

Avoid brittle tests that merely demand deleted class names remain absent unless there is a real regression risk. Prefer behavior and build-layout evidence.

---

# 12. Documentation cleanup

The archive branch preserves the full final QamHost implementation and its historical documentation.

On current `main`, remove dedicated QAMHost/QAM-injection documents that would otherwise look like active product architecture.

At minimum inspect dedicated documents such as:

```text
docs/QAM_SINGLE_ADDON_TAB_NATIVE_INNER_TABS_RESEARCH_AND_DESIGN_2026-09-13.md

docs/work-order/QAM_ADDON_SELECTED_CONDITIONAL_WIDTH_WORK_ORDER.md
docs/work-order/QAM_FRESH_OPEN_ADDON_TAB_SELECTION_CAUSAL_INTENT_WORK_ORDER.md
docs/work-order/QAM_NATIVE_01_STEAM_NATIVE_SURFACE_AND_INTERACTION_RECOVERY_WORK_ORDER.md
docs/work-order/QAM_NATIVE_02A_ALWAYS_OPEN_ADDON_TAB_HOTFIX_WORK_ORDER.md
docs/work-order/QAM_NATIVE_02_DEVICE_PROFILE_STABLE_TABS_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_PR2_QAM_FIVE_TAB_NATIVE_SHELL_WORK_ORDER.md
docs/work-order/ADDON_QUICK_SETTINGS_SHARED_SURFACE_QAM_REACT_SCOPE_FIX_WORK_ORDER.md
```

Pure-QAM implementation documents may be deleted from `main` because the archive branch is the retained history.

Do **not** mass-delete an unrelated historical work order only because it contains one old QAM sentence.

Update active architecture documents that still describe QAMHost as a supported frontend.

In particular inspect:

```text
docs/overlayui/README.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_ARCHITECTURE.md
docs/overlayui/ADDON_QUICK_SETTINGS_OVERLAY_UI_DESIGN.md
docs/shared-frontend/SHARED_FRONTEND_ARCHITECTURE_V2.md
docs/shared-frontend/ADDON_QUICK_SETTINGS_SHARED_SURFACE_ARCHITECTURE_2026-09-18.md
docs/shared-frontend/SHARED_FRONTEND_IMPLEMENTATION_PR_PLAN_V2.md
docs/appui/APP_UI_INFORMATION_ARCHITECTURE_2026-09-04.md
docs/VIIPER_MIGRATION_TODO.md
```

The active documentation must say clearly:

```text
Addon-owned Quick Settings surface = WinUI3 Overlay
Steam native QAM = Steam-owned native UI only
Steam native QAM invocation = existing SteamDeck QuickAccess pulse
No Addon QAM tab / QamHost / CDP / CEF patching
```

Historical Full1902 work orders that merely mention QAM closure as a frontend lifetime example do not need broad rewrites unless they now assert a false active architecture.

---

# 13. Comments and naming cleanup

Remove stale production comments that say or imply:

```text
QamHost is production
QAM frontend server exists
QAM and Overlay are peer Addon surfaces
QAM Addon-first tab selection is attempted
CEF remote debugging is prepared for future GamepadUI sessions
```

Update comments around shared frontend RPCs so they describe:

```text
Main UI / Overlay
```

instead of:

```text
Frontend / QAM / Overlay
```

Do not rename generic shared Quick Settings types merely because they were once used by QAM.

Do not rename `SteamQuickAccess` to `QAM`. The existing semantic name correctly represents the Steam Deck system button.

---

# 14. Repository-wide reference audit

Before finalizing, run a repository-wide search.

Production/build/test surfaces should have zero retired QamHost references:

```bash
rg -n   "SteamInputAddonforClaw\.QamHost|QamHostProcessController|CreateQamForCurrentUser|SelectAddonOnNextQuickAccessOpen|AcknowledgeQamSelectAddonOnNextOpenPrepared|_qamFrontendServer|_qamHostController"   src tests scripts SteamInputAddonforClaw.slnx
```

Expected result:

```text
no matches
```

Search for CEF bootstrap creation:

```bash
rg -n "SteamCefDebugBootstrap\.Ensure|\.cef-enable-remote-debugging" src tests
```

Expected result:

- no code path creates/ensures the marker;
- the marker string may remain only in the narrow ownership-safe legacy cleanup/tests.

Then prove the native Quick Access path still exists:

```bash
rg -n   "SteamQuickAccess|TryRequestQuickAccessPulse|RequestQuickAccessPulse|SteamDeckSystemButtonOverlay"   src tests
```

Expected result:

- front-button contract remains;
- presentation pulse remains;
- SteamDeck system-button overlay remains;
- tests remain.

A documentation search for `QAM` is allowed to retain references that explicitly mean **Steam's native Quick Access Menu**, but no active document may describe an Addon QAMHost/tab injection as a current feature.

---

# 15. Validation

Run the repository's normal build/test/release validation.

At minimum:

```text
dotnet build
full test suite
Release configuration tests
publish-layout script
publish asset verification
publish-size script/tests
```

Use the repository's existing exact CI commands where they differ from the generic examples above.

Required publish facts:

```text
qam/ directory absent
QamHost executable absent
qam.js absent
UI present
Overlay present
Runtime present
FSE Home artifacts present
required helpers/dependencies present
```

Required functional validation:

### Normal / Xbox360 presentation

```text
QuickSettingsOverlay action
  -> WinUI3 Overlay toggles

SteamQuickAccess
  -> not offered/valid in Normal domain under the existing capability contract
```

### SteamDeck presentation

```text
SteamButton
  -> existing Steam system-button pulse

SteamQuickAccess
  -> existing QuickAccess system-button pulse
  -> Steam native QAM toggles

QuickSettingsOverlay
  -> WinUI3 Overlay toggles
  -> no Steam React/CDP/QamHost involvement
```

### Runtime lifecycle

Verify no regression in:

```text
startup
shutdown
controlled Runtime restart
sleep / resume
Steam game enter/exit presentation switch
BPM enter/exit presentation switch
Overlay show/hide
Main UI <-> Overlay mutual exclusion
```

Do not add lifecycle machinery solely for theoretical event interleavings.

---

# 16. Likely files to delete or modify

This list is based on `main` at `e242978a48bf751424b7f8bd3de4dba2c4b1a525`. Re-scan current main before implementation.

## Delete

```text
src/SteamInputAddonforClaw.QamHost/**
src/SteamInputAddonforClaw/Lifecycle/QamHostProcessController.cs

tests/SteamInputAddonforClaw.Tests/QamFrontendBridgeTests.cs
tests/SteamInputAddonforClaw.Tests/QamFrontendContractTests.cs
tests/SteamInputAddonforClaw.Tests/QamHostCdpCommandCorrelatorTests.cs
tests/SteamInputAddonforClaw.Tests/QamHostCdpEvaluateResultTests.cs
tests/SteamInputAddonforClaw.Tests/QamHostGamepadUiTargetSelectorTests.cs
tests/SteamInputAddonforClaw.Tests/QamHostProcessControllerTests.cs

pure-QAM active design/work-order documents identified in section 12
```

## Modify

```text
SteamInputAddonforClaw.slnx

src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Steam/SteamCefDebugBootstrap.cs
  or replace it with the smallest ownership-safe legacy cleanup helper
src/SteamInputAddonforClaw/Install/UninstallBootstrap.cs
src/SteamInputAddonforClaw/Install/AddonDataPaths.cs only if comments require cleanup

src/SteamInputAddonforClaw.FrontendTransport/FrontendWire.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendServer.cs
src/SteamInputAddonforClaw.FrontendTransport/NamedPipeAddonFrontendClient.cs

scripts/publish-layout.ps1
scripts/verify-publish-assets.ps1
scripts/report-publish-size.ps1
scripts/tests/verify-publish-assets.tests.ps1
scripts/tests/report-publish-size.tests.ps1

shared tests that currently assert QAM-specific transport/coordination

active Overlay/shared-frontend/App UI architecture docs
```

## Preserve

```text
src/SteamInputAddonforClaw.Overlay/**
src/SteamInputAddonforClaw/VirtualOutput/Viiper/SteamDeckSystemButtonOverlay.cs
FrontButtonAction.SteamQuickAccess
FrontButtonAction.QuickSettingsOverlay
IMsiClawAddonPresentation.TryRequestQuickAccessPulse()
Full1902 presentation ownership/reconcile
shared Quick Settings product contracts used by Overlay/Main UI
```

---

# 17. Non-goals

Do not turn this into:

- a new Overlay architecture;
- a frontend framework rewrite;
- a controller-routing refactor;
- a VIIPER refactor;
- a Steam detection rewrite;
- a front-button mapping redesign;
- a settings migration framework;
- a generalized legacy-artifact migration system;
- a new process manager;
- a new Quick Access authority;
- a speculative race-hardening PR.

The objective is deletion and simplification.

---

# 18. Acceptance criteria

The PR is complete only when all of the following are true.

1. `archive/qamhost-final-2026-09-22` exists remotely and points to the verified final pre-retirement `main` SHA.
2. The complete `SteamInputAddonforClaw.QamHost` project is gone from current main.
3. `QamHostProcessController` and all Runtime process lifecycle wiring are gone.
4. No QAM-specific frontend named pipe is created.
5. The Addon-first QAM tab-selection notification/acknowledgement contract is gone.
6. Frontend protocol version is bumped once for the breaking removal.
7. Steam CEF remote-debugging marker creation is gone.
8. Existing Addon-owned CEF markers are cleaned up using ownership evidence only.
9. `qam/` is absent from release/publish output.
10. QamHost-only tests are gone.
11. `FrontButtonAction.SteamQuickAccess` still exists.
12. `SteamDeckSystemButtonOverlay.RequestQuickAccessPulse()` still exists.
13. `IMsiClawAddonPresentation.TryRequestQuickAccessPulse()` still controls pulse eligibility.
14. Steam Quick Access mapping invokes the native Quick Access pulse without QamHost coordination.
15. Steam native QAM still toggles under a live SteamDeck presentation.
16. `QuickSettingsOverlay` still invokes the Runtime-owned coordinated WinUI3 Overlay path.
17. Full1902 PID1902/HidHide/VIIPER/controller lifecycle behavior is unchanged.
18. Main UI and Overlay build/publish successfully.
19. Full tests pass.
20. Active architecture documentation describes WinUI3 Overlay as the only Addon-owned Quick Settings surface.

---

# 19. Final architecture statement

The post-retirement architecture must be explainable in one sentence:

> **The Addon owns one Quick Settings UI — the WinUI3 Overlay — while Steam's native QAM remains Steam-owned and is invoked only through the existing virtual Steam Deck Quick Access system-button pulse; no QamHost, GamepadUI patch, CDP, or CEF debugging integration remains in production.**

If the implementation needs a new authority, manager, scheduler, state machine, or Steam-internal integration to achieve this retirement, it is over-designed and should be simplified before merge.
