# Work Order — Full1902 Cleanup B: Remove Legacy Center M Dummy / MainUI Suppression Infrastructure

## Status

Focused deletion/simplification work order for removing the obsolete pre-Full1902 MSI Center M MainUI suppression architecture after Cleanup A removed the legacy Steam-session controller-routing authority.

This is a **cleanup PR**, not a controller-behavior redesign.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     6badcaca2abd4972cf9f09ba367ca533fc8c7d1d
latest merged production PR: #476 — Full1902 Cleanup A
```

Cleanup A already removed the old controller-routing owner, routing pipeline, route-scoped MSI native/input/HidHide wrappers, legacy SteamDeck output stage, old route-owned Win+G stage, dormant Game Bar presentation route, and obsolete Controller Status UI/contracts.

Cleanup B closes the next dead architecture graph: the dummy `MSI Center M.exe` process, MainUI mutex ownership, real-MainUI retirement/termination, OEM1 suppression lifecycle, and all support code/build/CI artifacts that exist only for that model.

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/FULL1902_POLICY_A2_DECOUPLE_FRONT_BUTTON_ACTIONS_AND_DISABLE_LEGACY_ROUTING_WORK_ORDER.md`
- `docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md`
- `docs/work-order/FULL1902_CLEANUP_A_REMOVE_LEGACY_STEAM_ROUTING_AUTHORITY_WORK_ORDER_V2.md`
- current PR3 / PR5–PR12 Full1902 authority, physical ownership, presentation, recovery, and stock-restoration work orders where relevant.

The application is pre-release. Do not keep an obsolete suppression architecture for source compatibility, historical tests, or a hypothetical future reuse.

---

# 1. Goal

Remove the old Center M suppression architecture whose model was roughly:

```text
Steam routing / OEM1 custom authority requested
    ↓
observe Center M backend + real MainUI
    ↓
track exact MSI Center M.exe process/window identity
    ↓
possibly minimize / retire / terminate real MainUI
    ↓
stage Addon dummy binary as "MSI Center M.exe"
    ↓
CREATE_SUSPENDED + private Job Object ownership
    ↓
own Local\MSI Center M.exe mutex
    ↓
verify same-name helper invariant
    ↓
poll helper / MainUI / backend lifecycle
    ↓
maintain suppression authority until routing/OEM1 release
```

That architecture no longer corresponds to the production controller model.

The current Full1902 model is:

```text
Center M Enabled
→ MSI / stock controller authority
→ startup roots Enabled / Automatic
→ physical PID1901
→ no Addon physical controller ownership
→ no Addon virtual controller presentation

Center M Disabled
→ Addon Runtime controller authority
→ Center M startup roots Disabled
→ physical PID1902
→ Addon-owned DirectInput
→ persistent deterministic HidHide baseline
→ one Addon-owned VIIPER runtime
→ exactly one live X360 or SteamDeck presentation
```

Front-button behavior is already independent:

```text
OEM1 / Event41
WING / Event88
→ MsiClawFrontButtonRuntime
→ feature-local WMI + gesture + action dispatch only

WING native Win+G suppression
→ WinGSuppressionGuard
→ bound to the entire Addon controller-authority lifetime
```

Therefore Full1902 no longer needs to impersonate MSI Center M, own MSI Center M's MainUI mutex, terminate MainUI for routing, or run an OEM1 helper lifecycle.

Target end state:

```text
no SteamInputAddonforClaw.CenterMHelper project
no SteamInputAddonforClaw.CenterMHelperSmoke project
no CenterMHelperSource publish payload
no dummy process renamed to "MSI Center M.exe"
no helper Job Object/process ownership graph
no Local\MSI Center M.exe mutex ownership
no legacy MainUI retirement/termination graph
no OEM1 suppression lifecycle coordinator or 2-second poll driver
no orphan-helper registry
no helper-specific CI smoke test
no helper-specific publish verification / size accounting

CenterMStartupHelper remains present and unchanged in purpose
Full1902 controller authority remains unchanged
OEM1/WING front-button actions remain current Full1902 feature-local behavior
```

---

# 2. Current production proof — why this cleanup is safe

## 2.1 PR #476 removed the only legacy routing composition

After Cleanup A, production no longer constructs:

```text
AddonRoutingRuntime
MsiClawRoutingComposition
CenterMMainUiRoutingGuardStage
legacy native/input/HidHide route stages
```

`AddonProcessHost` has no remaining production reference to the old Center M dummy/helper/MainUI suppression lifecycle.

The remaining old Center M graph is now source/build/test baggage rather than a controller authority.

## 2.2 Current front-button production code explicitly does not use the helper

Current production owner:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawFrontButtonRuntime.cs
```

Its Full1902 contract is already explicit:

```text
owns:
- OEM1 Event41 WMI observation
- WING Event88 WMI observation
- OEM1/WING gesture recognition
- mapping/action dispatch
- small WING lifetime epoch

never owns:
- PID1901/PID1902
- DirectInput
- HidHide
- VIIPER
- Steam/BPM observation
- controller recovery
- Center M startup authority
- legacy dummy MSI Center M helper
```

The current OEM1 startup path already relies on the Full1902 fact that Center M Disabled means the stock startup roots are disabled. It starts Event41 observation and grants gesture authority directly; it does not require a fake Center M process or MainUI suppression lifecycle.

Do not change that model in Cleanup B.

## 2.3 Current WING suppression is already owned by Full1902

PR #473 moved native WING `Win+G` suppression to:

```text
WinGSuppressionGuard
```

bound to the whole Center M Disabled / Addon controller-authority lifetime.

It is independent of:

- Steam game state;
- BPM;
- X360 vs SteamDeck presentation;
- old Center M helper/MainUI routing guard.

Cleanup B must not touch that ownership or reintroduce any helper as a WING prerequisite.

## 2.4 The old helper is exactly a dummy-process implementation

Current project:

```text
src/SteamInputAddonforClaw.CenterMHelper
```

publishes a NativeAOT executable that does nothing except remain alive. Runtime staging renames it to:

```text
MSI Center M.exe
```

The surrounding code then owns it through a retained process handle + private Job Object and combines that with the MSI MainUI mutex / same-name invariant.

This was a valid implementation for the earlier RE-derived suppression design, but it is not part of the current Full1902 controller authority.

## 2.5 Do not confuse this helper with the active startup helper

There are two unrelated projects with similar names:

```text
SteamInputAddonforClaw.CenterMHelper
→ OLD dummy process used for native MainUI/OEM1 suppression
→ DELETE in Cleanup B

SteamInputAddonforClaw.CenterMStartupHelper
→ CURRENT elevated helper for Center M startup-root Enable/Disable
→ KEEP
```

The current Full1902 authority contract depends on `CenterMStartupHelper` to mutate/verify the actual startup roots:

```text
MSI_Center_M_Server scheduled task
MSI_Center_M_Updater scheduled task
MSI Foundation Service startup mode
```

Cleanup B must never remove, rename, merge, repurpose, or route those operations through the old dummy helper.

---

# 3. Scope

## In scope

1. delete the old dummy Center M helper executable project;
2. delete the old helper smoke-test executable project;
3. remove the helper from the solution and main Runtime project references;
4. remove NativeAOT helper publish/copy/staging artifacts;
5. remove helper-specific CI and publish-verification logic;
6. delete the old helper ownership / invariant / orphan-retry implementation;
7. delete the old MainUI mutex / window / exact-process retirement and termination graph;
8. delete the old Center M backend polling and OEM1 suppression lifecycle coordinator/driver;
9. delete the obsolete MSI native-mode probe whose only consumer was the old MainUI retirement graph;
10. delete support interfaces/native wrappers whose production reference closure becomes empty;
11. delete architecture-only tests for the removed subsystem;
12. update current-facing documentation that still describes this helper/MainUI suppression graph as live production behavior.

## Out of scope

Do not redesign or change:

- Center M startup-root authority policy;
- `CenterMStartupHelper`;
- Disable Center M + Restart / Enable Center M + Restart behavior;
- PR12 stock-safe uninstall core;
- PR13 future Velopack/Windows uninstall interception;
- Full1902 physical PID1902 owner;
- DirectInput ownership;
- HidHide baseline;
- VIIPER ownership or X360/SteamDeck presentation;
- physical device loss / PnP recovery;
- suspend/resume controller recovery;
- WING `WinGSuppressionGuard` policy;
- final OEM1/WING button assignment policy;
- Overlay final button binding;
- developer vibration redesign;
- rumble, M1/M2, gyro, or other new features.

Do not create a replacement `CenterMMainUiManager`, `CenterMSuppressionService`, `FrontButtonAuthorityManager`, process watchdog, generalized process-inspection facade, or another abstraction to replace code being deleted.

---

# 4. Delete the old helper executable projects

Delete the entire projects:

```text
src/SteamInputAddonforClaw.CenterMHelper/
src/SteamInputAddonforClaw.CenterMHelperSmoke/
```

This includes their `.csproj` and `Program.cs` files.

The smoke project exists solely to prove that the staged dummy helper can be renamed, started through `CenterMHelperOwnership`, kept alive, and stopped through retained ownership. Once the helper architecture is deleted, preserving the smoke executable has no value.

Remove both project entries from:

```text
SteamInputAddonforClaw.slnx
```

Keep this project entry:

```text
src/SteamInputAddonforClaw.CenterMStartupHelper/SteamInputAddonforClaw.CenterMStartupHelper.csproj
```

---

# 5. Remove old helper build / publish / package plumbing

Current main Runtime project still contains a non-linked project reference to the dummy helper and explicitly NativeAOT-publishes it into `CenterMHelperSource`.

From:

```text
src/SteamInputAddonforClaw/SteamInputAddonforClaw.csproj
```

remove the old dummy-helper pieces:

```text
ProjectReference → SteamInputAddonforClaw.CenterMHelper
PublishCenterMHelper target
CopyCenterMHelperOutput target
CenterMHelperOutput item
CenterMHelperSource\CenterMHelper.exe copy
helper-specific comments/errors
```

Do **not** remove or modify the purpose of:

```text
ProjectReference → SteamInputAddonforClaw.CenterMStartupHelper
PublishCenterMStartupHelper
CopyCenterMStartupHelperOutput
```

Also keep unrelated helper/publish paths such as TDP helper packaging.

Expected publish delta:

```text
BEFORE
publish root/
  CenterMHelperSource/
    CenterMHelper.exe

AFTER
publish root/
  [no CenterMHelperSource directory required]
```

Do not replace the deleted payload with another dormant executable.

---

# 6. Remove helper-specific CI and publish tooling

## 6.1 Delete the dedicated smoke script

Delete:

```text
scripts/verify-centerm-helper-smoke.ps1
```

## 6.2 Remove the CI smoke step

From:

```text
.github/workflows/ci.yml
```

remove only:

```text
CenterM helper staged-artifact smoke test
```

Do not weaken the rest of Build/Test/Publish/Startup smoke validation.

## 6.3 Update publish asset verification

From:

```text
scripts/verify-publish-assets.ps1
```

remove:

```text
CenterMHelperSource\CenterMHelper.exe
```

from required assets and remove the old NativeAOT-sidecar check whose directory is specifically `CenterMHelperSource`.

Preserve all other current Runtime/UI/QAM/Overlay/HidHide/USBIP2/VIIPER/TDP asset checks.

Do not opportunistically change whether `CenterMStartupHelper` is a required-assets assertion unless a separate current packaging bug is demonstrated. Cleanup B's requirement is to preserve its existing output path, not redesign publish verification.

## 6.4 Update publish-size reporting

If current:

```text
scripts/report-publish-size.ps1
```

contains a dedicated `centermhelpersource/ → CenterM Helper` category, remove that now-dead category/branch and update its tests accordingly.

Do not change unrelated category accounting.

## 6.5 Update script regression tests

Adjust/remove helper-specific fixtures and expectations from current script tests, including reference closure such as:

```text
scripts/tests/verify-publish-assets.tests.ps1
scripts/tests/report-publish-size.tests.ps1
```

The post-cleanup tests must no longer create `CenterMHelperSource` merely to satisfy a deleted artifact contract.

---

# 7. Delete the helper ownership / staging / invariant graph

Delete after closing references:

```text
src/SteamInputAddonforClaw/CenterM/CenterMHelperOwnership.cs
src/SteamInputAddonforClaw/CenterM/CenterMHelperStaging.cs
src/SteamInputAddonforClaw/CenterM/CenterMHelperInvariant.cs
src/SteamInputAddonforClaw/CenterM/CenterMOrphanedHelperRegistry.cs
src/SteamInputAddonforClaw/CenterM/IHelperProcessNativeApi.cs
src/SteamInputAddonforClaw/CenterM/Win32HelperProcessNativeApi.cs
```

This intentionally removes concepts such as:

```text
HelperStartResult
HelperLivenessState
CREATE_SUSPENDED helper startup
private Job Object helper ownership
JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
retained dummy-process handle ownership
PartialCleanupUnconfirmed helper residue
same-name helper invariant
orphaned helper retry registry
runtime staging as "MSI Center M.exe"
```

These safeguards were necessary while the product intentionally created a fake same-name process. Once the product stops creating that process entirely, keeping the ownership/retry machinery is not additional safety; it is dead complexity.

Do not recreate equivalent functionality in a differently named wrapper.

---

# 8. Delete the legacy MainUI suppression / retirement graph

Delete after reference closure:

```text
src/SteamInputAddonforClaw/CenterM/CenterMMainUiRoutingGuard.cs
src/SteamInputAddonforClaw/CenterM/CenterMMainUiRoutingRetirement.cs
src/SteamInputAddonforClaw/CenterM/CenterMMainUiRoutingTerminator.cs
src/SteamInputAddonforClaw/CenterM/CenterMMainUiMutexOwnership.cs
src/SteamInputAddonforClaw/CenterM/CenterMMainUiWindowController.cs
src/SteamInputAddonforClaw/CenterM/MainUiLifecycleObserver.cs
src/SteamInputAddonforClaw/CenterM/MainUiWindowSnapshot.cs
src/SteamInputAddonforClaw/CenterM/SafeMainUiTerminator.cs
src/SteamInputAddonforClaw/CenterM/TrackedCenterMMainUi.cs
src/SteamInputAddonforClaw/CenterM/IProcessIdentityInspector.cs
src/SteamInputAddonforClaw/CenterM/Win32MainUiWindowSnapshotProvider.cs
```

Also remove any process-handle/opening interfaces or native helpers that become production-dead only because this graph is gone.

The old graph protected operations such as:

```text
find exact real MSI Center M.exe
retain exact process handle
prove identity/path/window state
minimize visible MainUI
wait for hidden state
verify XInput
terminate exact MainUI
stage dummy MainUI
own Local\MSI Center M.exe mutex
verify only owned helper has same process name
```

None of those operations belongs to current Full1902 steady-state controller ownership.

### Important safety distinction

Deleting the MainUI terminator does **not** mean replacing exact termination with broad process killing.

Required result is:

```text
Cleanup B performs no legacy MainUI suppression/termination at all.
```

Do not replace these classes with:

```text
Process.GetProcessesByName("MSI Center M") → Kill()
```

or any broader kill policy.

If future real-hardware evidence proves a targeted Center M runtime quiesce is required for a specific Full1902 recovery path, that must be designed against that current lifecycle and exact evidence in a separate focused PR. Do not preserve the old OEM1/routing MainUI subsystem as a speculative future mechanism.

---

# 9. Delete the old backend/OEM1 suppression lifecycle graph

Delete:

```text
src/SteamInputAddonforClaw/CenterM/CenterMOem1LifecycleCoordinator.cs
src/SteamInputAddonforClaw/CenterM/CenterMOem1LifecycleRuntime.cs
src/SteamInputAddonforClaw/CenterM/CenterMBackendProbe.cs
```

The current coordinator is a large state machine for the old suppression model, with concepts such as:

```text
Disabled
NeedsSetup
Reconciling
Armed
NativeMainUiActive
HiddenDebounce
FaultedNative
helper liveness polling
MainUI tracking/debounce
backend Launcher/Server readiness
shared helper demand
routing-guard coexistence
2-second lifecycle driver polling
```

That is not the current OEM1 product model.

Current OEM1 authority in Disabled mode is feature-local:

```text
MsiClawFrontButtonRuntime
→ Event41 observation
→ gesture recognition
→ current mapping domain from live Full1902 presentation
→ action dispatch
```

Do not migrate fields/state from `CenterMOem1LifecycleCoordinator` into `MsiClawFrontButtonRuntime`. Delete the obsolete suppression state instead.

Do not add a replacement timer or poller.

---

# 10. Delete old process-observation support after full reference closure

The following current types are shared mainly by the helper/MainUI/backend graph above. After deleting that graph, perform a fresh **non-test production reference check**:

```text
CenterMProcessNames
IProcessSnapshotSource / Win32ProcessSnapshotSource / ProcessSnapshotEntry
ICenterMNativeModeProbe
```

Current-code review at the baseline shows their production consumers are within the old helper/MainUI/backend suppression subsystem.

Therefore, if the fresh implementation branch confirms no current Full1902 consumer remains, delete them too.

Expected deletions include:

```text
src/SteamInputAddonforClaw/CenterM/CenterMProcessNames.cs
src/SteamInputAddonforClaw/CenterM/IProcessSnapshotSource.cs
src/SteamInputAddonforClaw/CenterM/ICenterMNativeModeProbe.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawCenterMNativeModeProbe.cs
```

`MsiClawCenterMNativeModeProbe` is explicitly an old read-only adapter created to let the MainUI-retirement logic inspect the same native-state manager as the former routing composition. It is not the Full1902 physical ownership primitive.

Do **not** delete current native controller primitives such as:

```text
MsiClawNativeStateManager
MsiClawModeController
native mode writer/resolver
MsiClawAddonPhysicalOwnership
```

Likewise, `Status/ControllerSoftwareStatusProviders.cs` has its own current stock-software identity facts. Do not delete current status functionality just because old `CenterMProcessNames` becomes dead.

---

# 11. Preserve the current Center M / OEM1 files that are still live

Do **not** delete the entire `src/SteamInputAddonforClaw/CenterM` directory.

At minimum, preserve current feature-local front-button infrastructure such as:

```text
IMsiEventSource.cs
WmiMsiEventSource.cs
CenterMOemSemantics.cs
Oem1ActionDispatcher.cs
Oem1ApplicationLauncher.cs
Oem1BigPictureLauncher.cs
Oem1EventGestureBridge.cs
Oem1GestureRecognizer.cs
Oem1KeyboardHotkeyExecutor.cs
Oem1MappingSlots.cs
```

and any current referenced action/mapping primitive found by fresh closure.

`CenterMOemSemantics` / `CenterMOemEventMapper` remains used by `WmiMsiEventSource` to classify Event41/Event88 and is part of current `MsiClawFrontButtonRuntime` input observation.

Preserve current OEM1/WING settings/contracts/frontend surfaces unless they directly reference the deleted suppression lifecycle rather than the current feature-local mapping implementation.

---

# 12. Preserve Full1902 controller authority exactly

Cleanup B must not alter these current owners or their ownership hierarchy:

```text
CenterMStartupControl / CenterMStartupStateReader
CenterMRebootAuthorityTransition
StockCenterMStartupBaseline
SteamInputAddonforClaw.CenterMStartupHelper

MsiClawAddonPhysicalOwnership
MsiClawInputSource
AddonControllerHidHideBaseline
MsiClawAddonPresentation
CanonicalViiperRuntime
CanonicalXbox360InputPublisher
CanonicalSteamDeckInputPublisher
SteamDeckSystemButtonOverlay
MsiClawFrontButtonRuntime
WinGSuppressionGuard
WindowsDeviceArrivalWatcher / current owned-controller recovery path
```

### Center M Enabled must remain

```text
startup roots = Enabled / Enabled / Automatic
controller authority = MSI / stock
physical desired PID = PID1901
Addon physical ownership = none
Addon controller HidHide ownership = none
Addon VIIPER presentation = none
```

### Center M Disabled must remain

```text
startup roots = Disabled / Disabled / Disabled
controller authority = Addon Runtime
physical desired PID = PID1902
DirectInput = Addon-owned
HidHide baseline = persistent Addon-owned
VIIPER = Addon-owned
exactly one live presentation
```

Steam/BPM remains presentation selection only:

```text
inactive → X360
active   → SteamDeck
```

No code in Cleanup B should change the above authority decision.

---

# 13. CenterMStartupHelper is a hard preservation boundary

This section is mandatory because the old and current helper project names are easy to confuse.

Preserve the complete current startup-helper chain:

```text
src/SteamInputAddonforClaw.CenterMStartupHelper/**
src/SteamInputAddonforClaw/CenterMStartup/**
```

including current production references/targets such as:

```text
CenterMStartupStateReader
CenterMStartupControl
CenterMStartupHelperClient
CenterMRebootAuthorityTransition
StockCenterMStartupBaseline
PublishCenterMStartupHelper
CopyCenterMStartupHelperOutput
```

The startup helper is the bounded elevated mutation boundary for the actual authority roots. It is not part of the old fake-MainUI design.

Do not consolidate both helpers into one project. Delete the obsolete one and leave the active one conceptually separate and obvious.

---

# 14. Lifecycle requirements

This cleanup removes a dead subsystem; it must not weaken current real handheld lifecycle behavior.

## 14.1 Sleep / Hibernate / Resume

While Center M is Disabled:

```text
suspend
→ Full1902 presentation/input quiesce behavior unchanged
→ no PID1901 authority release

resume
→ current Full1902 owned-controller recovery/reconcile unchanged
→ PID1902 reacquire/reclaim rules unchanged
→ WING Win+G suppression lifetime unchanged
```

No old OEM1 lifecycle resume participant should remain merely because it once implemented `IRuntimeResumeParticipant`.

## 14.2 Restart / Shutdown / Runtime restart

Center M Disabled remains durable authority:

```text
normal Windows shutdown/restart
→ do not restore PID1901 solely because process/OS exits
→ persistent HidHide policy unchanged

controlled Runtime restart
→ current process-owned resources retire safely
→ next Runtime reconciles PID1902 ownership
```

Removing dummy-helper Job Object cleanup does not weaken current controller safety because production no longer creates that dummy process.

## 14.3 Physical device loss / PnP re-enumeration

Current Full1902 recovery path remains authoritative.

Do not move any old Center M MainUI/process observation into physical-recovery code merely because those classes are being deleted.

## 14.4 Explicit stock restoration

`Enable Center M and Restart` and uninstall stock restoration must remain exactly current:

```text
neutral/retire presentation
release DirectInput
restore/verify PID1901
teardown VIIPER
remove Addon HidHide ownership
restore/verify Center M startup roots
restart / continue uninstall policy
```

Cleanup B must not substitute dummy-helper cleanup for actual startup-root restoration.

---

# 15. Tests — delete obsolete architecture tests, preserve current behavior tests

Delete tests whose only purpose is the removed dummy/helper/MainUI suppression architecture.

Known current candidates include, after fresh reference closure:

```text
CenterMHelperStagingTests.cs
CenterMHelperIntegrationTests.cs
CenterMBackendProbeTests.cs
CenterMMainUiRoutingGuardTests.cs
CenterMMainUiRoutingRetirementTests.cs
CenterMMainUiRoutingTerminatorTests.cs
CenterMMainUiWindowControllerTests.cs
CenterMOem1LifecycleCoordinatorTests.cs
SafeMainUiTerminatorTests.cs
TrackedCenterMMainUiTests.cs
```

Also delete any dedicated current tests discovered for:

```text
CenterMHelperOwnership
CenterMHelperInvariant
CenterMMainUiMutexOwnership
CenterMOrphanedHelperRegistry
IHelperProcessNativeApi / Win32HelperProcessNativeApi
IProcessIdentityInspector
MainUiLifecycleObserver
MsiClawCenterMNativeModeProbe
CenterMOem1LifecycleRuntime
```

Do not preserve a production type solely because an old test still instantiates it. Delete or migrate the test according to the surviving current product behavior.

### Keep / adapt current regressions

Retain current tests protecting:

```text
CenterMStartupStateReader / CenterMStartupControl / CenterMStartupHelperClient
CenterMRebootAuthorityTransition
StockCenterMStartupBaseline
PR12 stock-safe uninstall restoration

MsiClawAddonPhysicalOwnership
AddonControllerHidHideBaseline
MsiClawAddonPresentation
X360 ↔ SteamDeck switching
owned-controller PnP/recovery
suspend/resume authority

MsiClawFrontButtonRuntime
Event41 / Event88 parsing
OEM1 gesture/action mapping
WING gesture/action mapping
Steam/QAM pulse via current Full1902 presentation
Policy B WinGSuppressionGuard lifecycle
```

Required current-behavior regression after Cleanup B:

```text
Center M Disabled + supported MSI Claw
→ MsiClawFrontButtonRuntime can compose/start without ANY CenterMHelper/MainUI lifecycle type
```

and:

```text
Center M Disabled + WING
→ delivery remains gated by WinGSuppressionGuard readiness
→ no dummy helper dependency
```

Do not invent a new source-string test framework merely for this cleanup; use existing structural/unit tests and compile/reference closure where practical.

---

# 16. Documentation cleanup

Historical work orders and research documents are implementation history. Do not rewrite the whole repository history.

However, current-facing documents that still describe the dummy/helper/MainUI suppression architecture as active production behavior should be clearly marked superseded or updated to point to:

```text
Full1902 authority
MsiClawFrontButtonRuntime
WinGSuppressionGuard
Cleanup A / Cleanup B
```

Likely current-facing candidates include:

```text
docs/OEM1_CenterM_Runtime_Foundation_Status.md
docs/VIIPER_MIGRATION_TODO.md
```

If Cleanup A already added a sufficient superseded banner, update only what is still materially misleading after the B deletion. Avoid broad documentation churn.

Do not remove old research evidence merely because its implementation was superseded.

---

# 17. Required reference-closure gates

After implementation, run fresh source searches.

The following old production symbols should have **zero current source references outside historical docs if their files are deleted**:

```text
CenterMHelperOwnership
CenterMHelperStaging
CenterMHelperInvariant
CenterMOrphanedHelperRegistry
CenterMMainUiRoutingGuard
CenterMMainUiRoutingRetirement
CenterMMainUiRoutingTerminator
CenterMMainUiMutexOwnership
CenterMMainUiWindowController
CenterMOem1LifecycleCoordinator
CenterMOem1LifecycleRuntime
CenterMBackendProbe
SafeMainUiTerminator
TrackedCenterMMainUi
MainUiLifecycleObserver
MsiClawCenterMNativeModeProbe
IHelperProcessNativeApi
Win32HelperProcessNativeApi
CenterMHelperSource
```

For closure-dependent support types, zero-hit is expected if no current consumer remains:

```text
ICenterMNativeModeProbe
IProcessSnapshotSource
IProcessIdentityInspector
CenterMProcessNames
Win32MainUiWindowSnapshotProvider
```

### Important grep caveat

Do **not** use a naive requirement such as:

```text
git grep CenterMHelper → zero hits
```

because the valid current name:

```text
CenterMStartupHelper
```

must remain.

Search exact old symbols / paths instead, and explicitly confirm that `CenterMStartupHelper` still has production references.

Likewise, `CenterM` as a namespace/string is not expected to disappear because current OEM1/WMI and startup authority code legitimately remains.

---

# 18. Build / test / package validation

Required automated validation:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Also run the current script tests used by CI, including publish-asset and size-report regression tests after removing the helper fixture.

Run the current publish layout flow and verify:

```text
published Runtime succeeds
CenterMHelperSource is absent / not required
SteamInputAddonforClaw.CenterMStartupHelper output remains available exactly as before
TDP helper remains available
UI/QAM/Overlay assets remain valid
HidHide/USBIP2/VIIPER assets remain valid
startup process smoke test still passes
```

Do not retain the old `verify-centerm-helper-smoke.ps1` merely to keep CI count stable.

---

# 19. Manual MSI Claw smoke matrix

Hardware smoke is required when a supported device is available.

## A. Center M Enabled / stock authority

Verify:

```text
Center M startup roots = Enabled / Enabled / Automatic
physical controller = PID1901
no Addon physical ownership
no Addon VIIPER controller presentation
normal stock Center M behavior remains available
no dummy Addon "MSI Center M.exe" exists
```

## B. Center M Disabled / Addon authority

Verify:

```text
Center M startup roots = Disabled / Disabled / Disabled
physical = PID1902
DirectInput owned
HidHide baseline correct
one X360 presentation initially when Steam/BPM inactive
Steam/BPM → SteamDeck
Steam/BPM exit → X360
```

Also verify:

```text
OEM1 Event41 custom action path still works
WING Event88 custom action path still works
native Game Bar does not surface while Addon authority is active
no CenterMHelperSource runtime staging
no fake "MSI Center M.exe" process
no old MainUI mutex ownership
```

## C. Sleep / Resume

While Disabled:

```text
suspend/resume
→ Addon authority remains
→ current physical recovery converges safely
→ presentation restores according to current Steam/BPM fact
→ OEM1/WING remain functional
→ no helper/MainUI lifecycle is recreated
```

## D. Enable Center M + Restart

Verify existing explicit authority release:

```text
presentation retired
DirectInput released
PID1901 restored/verified
VIIPER torn down
Addon HidHide baseline removed
Center M startup roots restored
restart occurs
stock authority returns normally
```

## E. Uninstall / stock-safe cleanup

Confirm PR12 behavior remains valid and never expects the old dummy helper as part of stock restoration.

---

# 20. Failure-policy requirements

Cleanup B should primarily delete failure states that only existed because the product created the dummy helper/MainUI suppression graph.

Do not carry forward old failure taxonomy such as:

```text
HelperOwnershipUnresolved
HelperFailure
MutexFailure
same-name helper invariant failure
HiddenDebounce
FaultedNative
orphaned dummy-helper cleanup
```

into new Full1902 state.

Current failures continue to be handled by their actual owners:

```text
startup-root mutation/readback failure
→ CenterMStartup authority transition

PID/native/DirectInput failure
→ current physical owner/recovery path

HidHide failure
→ AddonControllerHidHideBaseline / fail-close policy

VIIPER/presentation failure
→ current presentation owner / fail-close policy

WING native suppression unavailable
→ WinGSuppressionGuard readiness / no unsafe WING custom delivery

OEM1 feature-local action failure
→ MsiClawFrontButtonRuntime's existing feature-local behavior
```

Do not invent a cross-cutting cleanup failure manager.

---

# 21. Overengineering guardrails

This cleanup should make the architecture simpler.

Do not add protection for unsupported/theoretical scenarios such as:

- another interactive user owning the MSI MainUI mutex;
- RDP/Fast User Switching authority arbitration;
- a hypothetical future app requiring the fake same-name process;
- instruction-level races between deleted MainUI/helper callbacks;
- keeping Job Object / exact-process tracking merely because those primitives are robust;
- an abstraction to allow the removed helper to return later.

Supported real lifecycle safety must remain unchanged:

- sleep / hibernate / resume;
- restart / shutdown / controlled Runtime restart;
- physical device loss / PnP re-enumeration;
- PID1901 ↔ PID1902 restoration/reclaim;
- HidHide ownership/cleanup;
- VIIPER ownership/teardown;
- actual operation failure;
- explicit stock restoration.

The criterion is:

> Does the deleted code protect a state the current Full1902 product can actually enter?

For the dummy/MainUI suppression subsystem, after Cleanup A + A2 + Policy B the answer is no. Delete it rather than preserving speculative complexity.

---

# 22. Completion checklist

Implementation is complete only when all are true:

- [ ] `SteamInputAddonforClaw.CenterMHelper` project deleted.
- [ ] `SteamInputAddonforClaw.CenterMHelperSmoke` project deleted.
- [ ] Both deleted projects removed from `SteamInputAddonforClaw.slnx`.
- [ ] Main Runtime `.csproj` no longer references/publishes/copies the dummy helper.
- [ ] `CenterMHelperSource` is no longer a publish contract.
- [ ] `scripts/verify-centerm-helper-smoke.ps1` deleted.
- [ ] CI helper-smoke step removed.
- [ ] Publish asset verifier no longer requires `CenterMHelperSource\CenterMHelper.exe`.
- [ ] Publish-size helper category/test fixture removed if now dead.
- [ ] `CenterMHelperOwnership`, staging, invariant, orphan-registry graph deleted.
- [ ] MainUI routing guard/retirement/termination/mutex/window graph deleted.
- [ ] OEM1 suppression lifecycle coordinator/runtime deleted.
- [ ] Dead backend/process/native-probe dependency closure deleted.
- [ ] `MsiClawCenterMNativeModeProbe` deleted if no current consumer remains.
- [ ] Architecture-only tests for deleted types removed.
- [ ] Current `MsiClawFrontButtonRuntime` OEM1/WING behavior preserved.
- [ ] Current `WinGSuppressionGuard` Policy-B ownership preserved.
- [ ] Current Full1902 physical/HidHide/VIIPER/recovery owners preserved.
- [ ] `SteamInputAddonforClaw.CenterMStartupHelper` remains present and production-referenced.
- [ ] `CenterMStartup/**` remains intact.
- [ ] Enable/Disable Center M reboot-bound authority semantics unchanged.
- [ ] PR12 stock-safe uninstall behavior unchanged.
- [ ] Debug build passes.
- [ ] Release build passes.
- [ ] Full Release tests pass.
- [ ] Publish layout succeeds without old helper payload.
- [ ] Current startup process smoke passes.
- [ ] `git diff --check` passes.
- [ ] Fresh reference search confirms no accidental old helper/MainUI production residue.

---

# 23. PR summary expectation

The PR description should state plainly that this is the second Full1902 architecture cleanup:

```text
Cleanup A removed legacy Steam-session controller-routing authority.
Cleanup B removes the now-unreferenced Center M dummy/MainUI suppression subsystem.
```

It should explicitly mention that the PR **does not remove `CenterMStartupHelper`** and does not change the current Full1902 controller authority, OEM1/WING mapping, or Win+G suppression behavior.

Report the source/build/test/package deletion closure and automated validation results. Do not claim physical MSI Claw validation if it was not performed.
