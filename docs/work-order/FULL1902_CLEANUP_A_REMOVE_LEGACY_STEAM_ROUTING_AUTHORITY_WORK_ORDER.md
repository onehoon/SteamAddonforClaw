# Work Order — Full1902 Cleanup A: Remove the Legacy Steam-Session Routing Authority

## Status

Focused cleanup work order for deleting the obsolete pre-Full1902 Steam-session controller-routing authority after the production cutover completed by PR #470 and PR #473.

This is a **deletion/simplification PR**, not a controller-behavior PR.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     536d5d3d25930dfd4231b76ed31a0ffeec3d6a11
latest merged production PR: #475 — Full1902 0903 status and diagnostic-log cleanup
```

Relevant completed cutovers already on `main`:

```text
PR #470 / 3c727cf194cfa9f2678468294f372dcc6791cdca
→ production stopped composing AddonRoutingRuntime
→ production stopped starting legacy Steam-session routing observation
→ OEM1/WING action ownership moved to MsiClawFrontButtonRuntime
→ Steam/QAM pulse delivery moved to the Full1902 presentation owner

PR #473 / 6bcc57e5e29020e8d242e36371c2611531f0bb45
→ WING / native Win+G suppression moved to Addon controller-authority lifetime
→ WinGSuppressionGuard became the production suppression primitive
→ WinGProtectionRoutingStage is no longer production authority

PR #475 / 536d5d3d25930dfd4231b76ed31a0ffeec3d6a11
→ Full1902 operational status became owner-aware
→ legacy compatibility/routing facts were intentionally retained for the existing frontend/status contract
```

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/FULL1902_POLICY_A2_DECOUPLE_FRONT_BUTTON_ACTIONS_AND_DISABLE_LEGACY_ROUTING_WORK_ORDER.md`
- `docs/work-order/FULL1902_POLICY_B_BIND_WING_GAMEBAR_SUPPRESSION_TO_ADDON_AUTHORITY_WORK_ORDER.md`
- `docs/work-order/FULL1902_0903_STATUS_AND_DIAGNOSTIC_LOG_CLEANUP_WORK_ORDER.md`
- current PR5–PR12 Full1902 physical-ownership / presentation / recovery / stock-release work orders where relevant.

The application is pre-release. Do not preserve an obsolete production architecture only for backward source compatibility.

---

# 1. Goal

Remove the dead controller-routing architecture whose authority model was:

```text
Steam game / BPM route desired
→ AddonRoutingRuntime
→ RoutingPipeline*
→ device-specific routing composition
→ route-scoped native-mode / DirectInput / HidHide stages
→ route-scoped SteamDeck output stage
```

Production no longer uses that authority model.

The current Full1902 product model is:

```text
Center M Enabled
→ MSI / stock controller authority
→ desired physical PID1901
→ no Addon physical controller ownership
→ no Addon VIIPER controller presentation

Center M Disabled
→ Addon Runtime controller authority
→ desired physical PID1902
→ persistent DirectInput ownership
→ deterministic Addon HidHide baseline
→ one persistent VIIPER runtime
→ exactly one live presentation

Steam/BPM inactive → Xbox360 presentation
Steam/BPM active   → SteamDeck presentation
```

Steam/BPM chooses only the virtual presentation. It does not own physical-controller authority.

Cleanup A must make the source tree match that already-shipped architecture.

Target end state:

```text
there is no AddonRoutingRuntime type
there is no legacy RoutingPipeline controller-ownership graph
there is no IHandheldRoutingComposition / HandheldRoutingCompositionFactory
there is no MsiClawRoutingComposition
there are no legacy route-scoped MSI native/input/HidHide stage wrappers
there is no legacy route-owned WinG stage
there is no legacy CanonicalSteamDeckOutputStage
there is no dormant Game Bar foreground → presentation route
AddonRuntimeHost no longer carries an always-null legacy routing backend contract
```

This PR is expected to have a large negative LOC count. That is acceptable. Do not split a single dead dependency graph into artificial micro-PRs merely to reduce deletion size.

---

# 2. Current production proof — why this cleanup is now safe

## 2.1 `AddonRuntimeCompositionFactory` already hard-disables the old runtime

Current `main` calls only:

```csharp
steamRuntime.StartActualObservation();
```

and then explicitly does:

```csharp
AddonRoutingRuntime? routingRuntime = null;
```

There is no production branch that calls `AddonRoutingRuntime.Create(...)`.

The stock PID1901 resume baseline is already gated independently by `stockCenterMAuthority`.

Therefore deleting `AddonRoutingRuntime` must not change Center M Enabled or Center M Disabled controller selection.

## 2.2 Full1902 owns the real controller lifecycle elsewhere

Current production controller owners live in `AddonProcessHost` and the Full1902 MSI components:

```text
MsiClawAddonPhysicalOwnership
MsiClawInputSource
AddonControllerHidHideBaseline
MsiClawAddonPresentation
CanonicalViiperRuntime
CanonicalXbox360InputPublisher
CanonicalSteamDeckInputPublisher
MsiClawFrontButtonRuntime
WinGSuppressionGuard
```

These are not replacements to create in this PR. They already exist and must remain the one production path.

## 2.3 Game Bar foreground presentation routing is already disconnected

`AddonProcessHost` still constructs:

```text
GameBarForegroundWatcher
GameBarForegroundPresentationDelivery
```

and still contains dead callbacks into:

```text
AddonRuntimeHost.HandleGameBarForegroundChangedAsync(...)
```

but production does not start the watcher and does not subscribe its foreground event.

Existing tests explicitly guard that the old automatic Game Bar foreground → Xbox360 presentation path remains disconnected.

Delete the dead path instead of preserving a permanently-disconnected watcher/delivery graph.

## 2.4 `AddonRuntimeHost` still models a backend that production can never have

Despite production always passing `null`, `AddonRuntimeHost` still owns:

```text
AddonRoutingRuntime? _routingRuntime
routing reconcile
routing shutdown/dispose
residual route cleanup on resume
preserved route recovery on resume
routing auxiliary power participants
routing status capture
Game Bar foreground routing
legacy developer vibration routing
routing-specific termination snapshots
```

Cleanup A must remove that obsolete optional-backend model rather than replacing it with another null-object wrapper.

---

# 3. Scope boundary

## 3.1 In scope

This PR removes:

1. legacy generic routing authority/orchestration;
2. legacy MSI routing composition and route-scoped wrappers;
3. legacy route-owned SteamDeck output wrapper;
4. legacy route-owned Win+G stage;
5. dormant Game Bar foreground presentation selection;
6. dead routing branches from `AddonRuntimeHost` / power orchestration;
7. tests that exclusively verify deleted architecture;
8. obsolete production parameters/callbacks whose only consumer was that architecture.

## 3.2 Explicitly out of scope — Cleanup B

Do **not** delete the entire old Center M dummy/helper suppression subsystem in this PR.

Cleanup B is reserved for the separate packaging/process-helper cluster, including as applicable after a fresh reference check:

```text
SteamInputAddonforClaw.CenterMHelper project
CenterMHelperSource packaging/publish targets
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
old Center M helper/process ownership tests
```

One exception is allowed in Cleanup A:

```text
CenterMMainUiRoutingGuardStage.cs
```

is a pure `IRoutingPipelineStage` wrapper around that subsystem. Because Cleanup A removes `IRoutingPipelineStage`, delete this wrapper now. Keep the underlying Center M helper/guard classes for Cleanup B.

Do not use the existence of the Cleanup-B classes as a reason to retain the entire legacy routing interface.

## 3.3 Other explicit non-goals

Do not use this PR to implement or redesign:

- final WING/OEM1 user mapping policy;
- Overlay button assignment;
- M1/M2 X360 remapping;
- rumble gain or new Full1902 rumble routing;
- battery charge limit;
- installer/uninstaller PR13 integration;
- Center M startup-root semantics;
- Full1902 status-contract redesign;
- frontend protocol redesign;
- Steam/BPM detection semantics;
- PID1901/PID1902 transition algorithms;
- HidHide ownership policy;
- VIIPER ownership policy;
- physical recovery policy;
- a new power manager;
- a new authority manager.

---

# 4. Required deletion A — legacy `Routing` authority graph

Delete the old routing-runtime/orchestration files after closing references:

```text
src/SteamInputAddonforClaw/Routing/AddonRoutingRuntime.cs
src/SteamInputAddonforClaw/Routing/IRoutingSafetySession.cs
src/SteamInputAddonforClaw/Routing/RoutingPipelineExecution.cs
src/SteamInputAddonforClaw/Routing/RoutingPipelinePlan.cs
src/SteamInputAddonforClaw/Routing/RoutingPipelineRuntimeCoordinator.cs
src/SteamInputAddonforClaw/Routing/RoutingPipelineSessionCoordinator.cs
src/SteamInputAddonforClaw/Routing/RoutingReconcileStatusRefresh.cs
```

This deletes the old concepts such as:

```text
IRoutingPipelineStage
IRoutingPipelineExecutor
RoutingPipelinePlan
ActiveRoutingPipelineSession
PendingRoutingPipelineCleanup
IRoutingRuntimeSessionBoundaryParticipant
IRoutingSafetySession
RoutingRuntimeTerminationSnapshot
RoutingActionKind
legacy route reconcile/fail-close ownership
legacy route shutdown backend
```

Do not create renamed replacements for these concepts.

Full1902 already has its own explicit physical owner, presentation owner, recovery owner, and stock-release path.

---

# 5. Required preservation inside the `Routing` namespace

Do **not** delete the whole `Routing` directory.

## 5.1 Keep `RoutingEligibilityPolicy.cs`

Current `SystemStatusProvider` still uses `RoutingEligibilityPolicy` to publish existing legacy/stock compatibility and routing-eligibility facts.

PR #475 deliberately preserved those facts while overriding only the final Full1902 operational `AddonStatus` when current owners positively prove a healthy Disabled-mode controller.

Cleanup A must not redesign that status model.

Keep:

```text
src/SteamInputAddonforClaw/Routing/RoutingEligibilityPolicy.cs
```

## 5.2 Keep `RoutingRuntimeStatusSnapshot.cs` as temporary frontend/status compatibility

The current frontend mapper/control/tests still consume:

```text
RoutingRuntimeStatusSnapshot
```

Production has already effectively exposed it as unavailable because no `AddonRoutingRuntime` exists.

Keep the type in Cleanup A and preserve the existing passive result:

```csharp
RoutingRuntimeStatusSnapshot.Unavailable
```

Do not reinterpret it as Full1902 presentation state in this PR.

A later status/frontend cleanup may remove or replace this historical contract.

## 5.3 Move only `RoutingOperationalState` out of the deleted session coordinator

Today:

```csharp
internal enum RoutingOperationalState { Passive, OverrideActive }
```

is declared inside `RoutingPipelineSessionCoordinator.cs`, but `RoutingRuntimeStatusSnapshot` still needs the enum for the existing frontend/status contract.

When deleting `RoutingPipelineSessionCoordinator.cs`, move this small enum mechanically to:

```text
RoutingRuntimeStatusSnapshot.cs
```

or another already-existing status-adjacent file in the same namespace.

Do not keep the session coordinator merely to host one enum.

Do not create a new routing state manager or abstraction.

## 5.4 Keep `RoutingTraceContext.cs`

Do not delete or rename it in Cleanup A.

Current nonlegacy diagnostics still reference it, including the SteamDeck virtual-device identity diagnostics and current physical-input diagnostics.

The name can be reconsidered later only if there is value beyond cosmetic cleanup.

---

# 6. Required deletion B — legacy handheld routing composition

Delete:

```text
src/SteamInputAddonforClaw/Devices/Abstractions/IHandheldRoutingComposition.cs
src/SteamInputAddonforClaw/Devices/HandheldRoutingCompositionFactory.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawRoutingComposition.cs
```

These files exist to create and describe the exact backend graph that `AddonRoutingRuntime` used.

After PR #470 there is no production caller.

Do not replace them with:

```text
IControllerAuthorityComposition
IFull1902RoutingComposition
MsiClawControllerManager
MsiClawRoutingService
```

Full1902 already has direct, explicit owners. A new wrapper would only preserve the old abstraction shape under a new name.

---

# 7. Required deletion C — route-scoped MSI stage/session wrappers

Delete the legacy wrappers whose reason for existence was the old routing pipeline:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawNativeModeSessionCoordinator.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawNativeModeStage.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawHidHideBaselineStage.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawPhysicalInputStage.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawPhysicalIsolationStage.cs
src/SteamInputAddonforClaw/CenterM/CenterMMainUiRoutingGuardStage.cs
```

These are **not** the current Full1902 owners.

## 7.1 Preserve the low-level physical primitives Full1902 actually uses

Do not delete or materially alter:

```text
MsiClawDeviceAdapter
MsiClawNativeStateManager
MsiClawModeController / native mode writer/resolver primitives
MsiClawInputSource
MsiClawInputContracts
DirectInput enumerator/topology primitives
MsiClawAddonPhysicalOwnership
AddonControllerHidHideBaseline
```

`MsiClawNativeStateManager` is still shared by active production paths such as stock PID1901 baseline and Full1902 physical ownership/recovery.

`MsiClawInputSource` is the current process-lifetime PID1902 input source.

Delete the obsolete wrappers, not the hardware primitives.

## 7.2 Do not delete rumble primitives merely because the old composition used them

`MsiClawRumbleSink` and its endpoint/transport contracts are not required to be deleted in Cleanup A.

They are isolated primitives and may be used by later Full1902 rumble work.

Do not expand Cleanup A into the planned rumble redesign.

If deleting a route wrapper exposes a purely route-only rumble adapter with zero non-test/future-primitive value, document that separately in the PR before deleting it. Do not casually delete the reusable transport layer.

---

# 8. Required deletion D — old virtual-output routing stage

Delete:

```text
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckOutputStage.cs
```

This class is the old route-scoped SteamDeck output owner implementing `IRoutingPipelineStage`.

Full1902 production presentation authority is now:

```text
MsiClawAddonPresentation
→ one CanonicalViiperRuntime
→ persistent X360 + SteamDeck logical devices
→ exactly one attached/live presentation
→ CanonicalXbox360InputPublisher or CanonicalSteamDeckInputPublisher
```

PR #470 also moved Steam/QAM pulse requests into the current presentation owner through one shared `SteamDeckSystemButtonOverlay`.

Therefore do not preserve `CanonicalSteamDeckOutputStage` for pulse delivery, developer test mode, or WING.

Preserve:

```text
CanonicalViiperRuntime
CanonicalSteamDeckSession if still consumed by current presentation implementation
CanonicalSteamDeckInputPublisher
CanonicalXbox360InputPublisher
SteamDeckSystemButtonOverlay
current VIIPER attach/detach/query primitives
```

Do not create a second publisher/device to replace the deleted stage.

---

# 9. Required deletion E — legacy route-owned Win+G stage

Delete:

```text
src/SteamInputAddonforClaw/GameBar/WinGProtectionRoutingStage.cs
```

PR #473 made this obsolete in production.

Keep:

```text
src/SteamInputAddonforClaw/GameBar/WinGSuppressionGuard.cs
```

and preserve the current Policy-B lifecycle exactly:

```text
NativeMessageLoop is pumping
→ AddonProcessHost.StartRuntimeEventWatchers()
→ the ONE WinGSuppressionGuard.Start() installation site

Center M Disabled deferred startup
→ acquire/prove Full1902 physical ownership
→ EnsureArmed()
→ IsArmed == true
→ only then first live virtual presentation
→ only then front-button runtime
```

Do not move `WinGSuppressionGuard.Start()` out of the pumping message-loop thread during this cleanup.

Do not add a second hook or replacement suppression service.

---

# 10. Required deletion F — dormant Game Bar foreground presentation path

The old Game Bar foreground path is already disconnected by Full1902 policy and current tests.

Remove it completely.

Delete:

```text
src/SteamInputAddonforClaw/GameBar/GameBarForegroundWatcher.cs
```

Remove from `AddonProcessHost`:

```text
_gameBarForegroundWatcher
_gameBarDelivery
GameBarForegroundPresentationDelivery
constructor creation/wiring
shutdown StopAccepting/unsubscribe/dispose calls
OnGameBarForegroundChanged(...)
RequestGameBarPresentationReconcile()
```

Remove from `AddonRuntimeHost`:

```text
HandleGameBarForegroundChangedAsync(...)
```

There must be no replacement automatic rule such as:

```text
Game Bar foreground → Xbox360
Game Bar foreground → presentation pause
Game Bar foreground → VIIPER attach/detach
```

Full1902 presentation selection remains only:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

Native Xbox Game Bar suppression remains Policy B and is independent of foreground detection.

---

# 11. Required simplification G — remove the legacy routing backend from `AddonRuntimeCompositionFactory`

Current factory has already proven the old runtime is impossible by assigning:

```csharp
AddonRoutingRuntime? routingRuntime = null;
```

Delete that variable and the log line whose only purpose is announcing that the dead runtime was not composed.

Construct `AddonRuntimeHost` without a routing backend parameter.

Also remove factory parameters that become genuinely unused after this cleanup. Current candidates include the old routing-only composition inputs such as:

```text
handheldDeviceAdapter        — if no remaining factory use
hardwareSupported            — if no remaining factory use
winGSuppressionGuard         — if no remaining factory use
routingReconcileCompleted    — if no remaining factory use
```

Do not remove parameters that still feed current status, recovery, stock baseline, startup settings, or Full1902 behavior.

In particular, preserve the independent stock-authority resume baseline:

```csharp
stockCenterMAuthority
+ stockCenterMBaseline
→ PID1901 stock baseline on stock-authority resume
```

Center M Disabled must still not run that stock PID1901 baseline.

Do not reintroduce a `legacyRoutingAllowed` interpretation as physical-routing permission.

---

# 12. Required simplification H — slim `AddonRuntimeHost` to its current responsibilities

After Cleanup A, `AddonRuntimeHost` remains useful. Do not delete it.

Its retained responsibilities are conceptually:

```text
SteamSessionRuntime ownership
ActualRunningAppId observation
raw Steam/BPM presentation snapshot exposure
DeveloperTestModeState ownership/facts
suspend/resume notification registration
stock-authority resume baseline orchestration
PowerResumeObserved delivery used by Full1902 process-host recovery
orderly Steam/power watcher disposal
```

Remove the old routing-backend responsibilities.

## 12.1 Remove fields/contracts that exist only for `AddonRoutingRuntime`

Remove as applicable after reference closure:

```text
AddonRoutingRuntime? _routingRuntime
ResumeFreshReconcileSuppression
RoutingShutdownSucceeded
ShouldDisposeRoutingBackend(...)
ShouldSchedulePostCommitFreshReconcile(...)
_routingShutdownOverride
_routingDisposeOverride
_routingReconcileCompleted
_preservedResumeDeferredReconcile
routing background reconcile tasks used only by the old route
```

Do not keep an `ILegacyRoutingBackend` or null-object replacement.

## 12.2 Remove old routing operations

Remove:

```text
ReconcileAsync(...)
ReconcileFreshAfterResumeAsync(...)
ReconcilePreservedRoutingSessionAsync(...)
DrainPreservedResumeDeferredReconcileAsync(...)
QueueDeferredRoutingReconcile(...)
legacy routing status refresh requests
legacy routing shutdown/dispose path
legacy routing cancellation callback
```

If `StatusRefreshRequested` has no remaining producer once route reconciliation is deleted, remove the event and the corresponding frontend subscription rather than retaining a permanently-never-fired event.

## 12.3 Preserve real resume behavior

Do not break:

```text
Center M Enabled
→ suspend/resume notification
→ stock PID1901 baseline verification/recovery

Center M Disabled
→ PowerResumeObserved
→ existing AddonProcessHost Full1902 owned-controller resume/recovery path
```

Cleanup A is not permission to route Disabled-mode resume through the old stock baseline.

## 12.4 Preserve real termination safety, remove only routing-derived inputs

Current `UserTerminationGuard` consumes a `RoutingRuntimeTerminationSnapshot` plus old native-route/recovery facts.

Once `RoutingRuntimeTerminationSnapshot` is deleted, do not retain it solely to satisfy this guard.

Remove the legacy routing-session inputs from the lower termination decision.

However, preserve the currently real Full1902 process-safety gates:

```text
Center M Disabled → ControllerAuthorityMandatory
_disabledControllerStartupPending != 0 → authority transition is blocked
real incomplete-recovery mutation safety if still consumed by current startup/recovery path
```

`AddonProcessHost` currently uses `UserTerminationBlockReason.RoutingTransition` as the existing reason while the deferred Disabled controller startup is committing. Do not weaken that gate merely because the enum name is historical.

Prefer the smallest simplification:

- keep `UserTerminationDecision`, `MandatoryControllerRuntimePolicy`, and `UserTerminationComposition`;
- remove the obsolete routing snapshot/native-route predicates;
- retain any lower recovery-mutation predicate only if it is still backed by a real current `RecoveryManager` condition.

Do not invent a new termination manager.

---

# 13. Required simplification I — remove routing-only branches from power orchestration

`PowerTransitionCoordinator` is still used for real suspend/resume lifecycle handling, so do **not** delete it merely because many of its callbacks were originally added for routing.

But the following concepts are legacy-route-specific and should be removed when their only production caller disappears:

```text
hasResidualRoutingCleanup
retryResidualRoutingCleanup
hasPreservedRoutingSession
reconcilePreservedRoutingSession
afterPreservedRecoveryCommit
post-commit deferred routing reconcile
routing-specific log messages for those branches
```

Update `PowerTransitionTests` accordingly:

- delete tests whose only subject is residual/preserved legacy routing sessions;
- retain tests for real suspend deadline handling, power epoch/barrier safety, recovery gating, stock baseline behavior, notification ordering, and generic participant handling where still applicable.

Do not rewrite the power coordinator into a new framework.

Do not add an epoch/barrier beyond the existing power lifecycle machinery.

The purpose is to remove route-only branches from the existing owner, not create another owner.

---

# 14. Frontend/status compatibility during Cleanup A

Cleanup A must not turn into a frontend/status redesign.

## 14.1 Keep the current routing-status wire semantics passive/unavailable

`InProcessAddonFrontendControl` and `FrontendSnapshotMapper` still consume `RoutingRuntimeStatusSnapshot`.

After `AddonRuntimeHost.CaptureRoutingStatus()` is removed, make the current compatibility source explicit and simple:

```csharp
RoutingRuntimeStatusSnapshot.Unavailable
```

Preferred shape:

- the frontend control's default routing-status provider returns `Unavailable` when no explicit test provider is supplied; or
- production passes `() => RoutingRuntimeStatusSnapshot.Unavailable` directly.

Do not create a fake routing runtime just to populate this snapshot.

Do not map Full1902 `ActivePresentation` into `SteamOutputActive` in this cleanup.

## 14.2 Preserve PR #475 Full1902 operational status behavior

A healthy Center M Disabled Full1902 session must still be able to report:

```text
AddonStatus = Ready
```

through the PR #475 owner-aware override while legacy compatibility may separately remain:

```text
Compatibility = Unsupported / MsiCenterMNotOperational
RoutingDecision = existing legacy fact
RoutingRuntimeStatusSnapshot = Unavailable
```

That combination is currently intentional.

## 14.3 Developer vibration remains unchanged/unavailable

The old developer vibration path currently delegates through the always-null `AddonRoutingRuntime`, so production result is already unavailable.

When deleting:

```text
AddonRuntimeHost.RunDeveloperVibrationTestAsync(...)
AddonRuntimeHost.CancelDeveloperVibrationTest()
CanonicalSteamDeckOutputStage developer vibration path
```

preserve the same user-visible unavailable/no-op result in the frontend diagnostic surface.

Do not wire developer vibration into Full1902 in Cleanup A.

Full1902 rumble/feedback work is separate.

---

# 15. Tests to delete or migrate

Delete tests whose only value is verifying deleted architecture.

At minimum inspect and remove/update the dedicated tests for:

```text
AddonRoutingRuntime
HandheldRoutingCompositionFactory
IHandheldRoutingComposition-backed fake compositions
RoutingPipelineExecution / plan
RoutingPipelineRuntimeCoordinator
RoutingPipelineSessionCoordinator
RoutingReconcileStatusRefresh
MsiClawRoutingComposition
MsiClawNativeModeSessionCoordinator
MsiClawNativeModeStage
MsiClawHidHideBaselineStage
MsiClawPhysicalInputStage
MsiClawPhysicalIsolationStage
CenterMMainUiRoutingGuardStage wrapper
WinGProtectionRoutingStage
CanonicalSteamDeckOutputStage
GameBarForegroundWatcher
GameBarForegroundPresentationDelivery
```

Known current test files include, but are not limited to:

```text
tests/SteamInputAddonforClaw.Tests/AddonRoutingRuntimeTests.cs
tests/SteamInputAddonforClaw.Tests/HandheldRoutingCompositionFactoryTests.cs
tests/SteamInputAddonforClaw.Tests/RoutingPipelineRuntimeCoordinatorTests.cs
tests/SteamInputAddonforClaw.Tests/RoutingReconcileStatusRefreshTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawPhysicalInputStageTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawPhysicalIsolationStageTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawNativeModeSessionCoordinatorTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawRoutingCompositionTests.cs
tests/SteamInputAddonforClaw.Tests/MsiClawRoutingCompositionOem1ActionPathTests.cs
tests/SteamInputAddonforClaw.Tests/GameBarForegroundWatcherTests.cs
```

Use a fresh `git grep` / solution search before deleting so every dedicated test of a removed type is closed.

Do not delete a mixed test file wholesale if it also protects a current Full1902 primitive. Move or retain the still-relevant tests instead.

---

# 16. Regression tests that MUST remain green

The deletion is correct only if current product behavior remains protected.

Preserve/update tests covering:

## 16.1 Policy A2

```text
Center M Enabled + Steam active
→ no legacy physical routing mutation

Center M Enabled + BPM active
→ no legacy physical routing mutation

Developer Test Mode
→ never becomes a Full1902 presentation-selection input

MsiClawFrontButtonRuntime
→ no dependency on AddonRoutingRuntime / RoutingPipeline / old output stage

OEM1 domain
→ derived from actual Full1902 ActivePresentation

WING Steam pulse
→ delivered only through MsiClawAddonPresentation pulse seam
```

Where an existing source-string test currently asserts that a legacy type is merely not instantiated, strengthen/adapt it to the new reality that the type has been deleted.

Do not add a large new structural-test framework.

## 16.2 Policy B

Keep all current safety coverage for:

```text
one WinGSuppressionGuard.Start() installation site
hook installation from the pumping message-loop thread
Disabled startup deferred off the hook thread
arm/prove before first live presentation
arm failure fail-closed
suppression retained across X360↔SteamDeck
suppression retained across Overlay capture
suppression retained across physical recovery
suppression released only at verified stock-authority restoration boundary
Center M Enabled does not arm Addon-authority suppression
```

Deleting `WinGProtectionRoutingStage` must not weaken any of these.

## 16.3 Full1902 physical lifecycle

Keep the current PR5–PR10 coverage for realistic production lifecycle:

```text
Disabled boot admission
PID1902 acquisition
exact DirectInput device selection
persistent HidHide baseline
first presentation attach
X360 ↔ SteamDeck switching
physical DirectInput loss
same-device PnP re-enumeration
owned PID1901 drift reclaim
suspend / hibernate / resume
recovery fail-close
cleanup uncertainty fail-close
```

## 16.4 Stock restoration / uninstall core

Keep PR3/PR12 coverage for:

```text
front-button teardown
presentation retirement
DirectInput release
same physical MSI Claw PID1901 proof
Enabled-mode HidHide cleanup
Center M startup-root enable/readback
Win+G suppression release only after verified stock-safe boundary
```

## 16.5 PR #475 status behavior

Keep tests proving:

```text
healthy Full1902 Disabled owner → final AddonStatus Ready
stock compatibility facts remain unchanged
routing runtime compatibility snapshot remains passive/unavailable
status capture does not mutate controller state
```

---

# 17. Reference-closure validation

Before considering the PR complete, run source searches over `src/` and `tests/`.

Production source should have no remaining references to deleted authority types such as:

```text
AddonRoutingRuntime
IHandheldRoutingComposition
HandheldRoutingCompositionFactory
MsiClawRoutingComposition
IRoutingPipelineStage
IRoutingPipelineExecutor
RoutingPipelinePlan
RoutingPipelineRuntimeCoordinator
RoutingPipelineSessionCoordinator
IRoutingSafetySession
RoutingRuntimeTerminationSnapshot
MsiClawNativeModeSessionCoordinator
MsiClawNativeModeStage
MsiClawHidHideBaselineStage
MsiClawPhysicalInputStage
MsiClawPhysicalIsolationStage
CenterMMainUiRoutingGuardStage
WinGProtectionRoutingStage
CanonicalSteamDeckOutputStage
GameBarForegroundWatcher
GameBarForegroundPresentationDelivery
HandleGameBarForegroundChangedAsync
```

Historical design/work-order documents are not production references and do not need a broad rewrite.

Do not perform a repository-wide historical-document purge in this PR.

If a **newer-than-baseline** non-test production reference has appeared by implementation time, stop and evaluate whether it represents current Full1902 behavior before deleting it. Do not blindly satisfy this list against a changed main branch.

---

# 18. Preserve these current owners explicitly

The PR must leave one clear current controller architecture.

Do not delete or duplicate:

```text
CenterMStartupControl / Center M startup-root authority
WindowsTaskSchedulerStartupManager mandatory Runtime startup policy
MsiClawNativeStateManager
StockCenterMStartupBaseline
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
WindowsDeviceArrivalWatcher / current owned-device recovery entrypoint
CenterMRebootAuthorityTransition
PR12 stock-safe uninstall preparation
SteamSessionRuntime Actual AppID/BPM facts
```

No second authority, wrapper, manager, inspector, or recovery graph is needed.

---

# 19. Lifecycle acceptance criteria

Cleanup A is acceptable only if all of the following remain true.

## Center M Enabled

```text
startup roots Enabled/Automatic
→ stock controller authority
→ PID1901 baseline remains stock-safe
→ Steam game/BPM changes do not cause PID1902 takeover
→ no Full1902 DirectInput/HidHide/VIIPER controller ownership
→ no Policy-B Addon-authority WING suppression
```

## Center M Disabled

```text
mandatory Runtime
→ PID1902 physical owner
→ persistent DirectInput session
→ deterministic HidHide baseline
→ one VIIPER runtime
→ exactly one presentation live
→ WING Win+G suppression tied to Addon authority
```

## Steam/BPM transitions

```text
inactive → Xbox360
active   → SteamDeck
```

with no physical-controller authority transition.

## Sleep / hibernate / resume

```text
Center M Disabled
→ Addon authority remains selected
→ existing Full1902 resume/recovery path continues

Center M Enabled
→ stock PID1901 resume baseline remains available
```

## Physical loss / PnP re-enumeration

Current Full1902 owner/recovery path remains the only recovery authority. No deleted routing coordinator may be reintroduced as a fallback.

## Restart / shutdown

Center M Disabled remains reboot-bound Addon authority. Controlled Runtime restart must not restore stock merely because the process is exiting.

## Enable Center M / uninstall stock release

Existing verified stock-restoration ordering remains unchanged.

---

# 20. Overengineering exclusions

Do not add complexity to compensate for deleting dead code.

Specifically, do **not** add:

- `LegacyRoutingCompatibilityManager`;
- `ControllerAuthorityRouter`;
- another routing interface hierarchy;
- another power coordinator;
- another recovery coordinator;
- another state cache mirroring Full1902 owners;
- a routing epoch/barrier to replace deleted route epochs;
- a null routing backend implementation;
- a compatibility wrapper whose only job is preserving old tests;
- retry machinery for theoretical timing interleavings;
- support for Fast User Switching / RDP / multi-session.

Evaluate races under the real product lifecycle only.

A blocker must have a plausible production path involving supported lifecycle such as:

```text
Sleep / Hibernate / Resume
Restart / Crash / Shutdown
physical device loss / PnP re-enumeration
routing/authority rollback or fail-close
HidHide ownership/teardown
VIIPER ownership/teardown
PID1901 ↔ PID1902 restoration
real operation failure
```

Do not defend pathological instruction-level interleavings merely because a synthetic test can create them.

---

# 21. Implementation order

Recommended order to keep the diff understandable:

```text
1. Re-check main against the baseline and close new references.
2. Simplify AddonRuntimeCompositionFactory so no routingRuntime variable/parameter exists.
3. Slim AddonRuntimeHost and remove routing-only power/reconcile/shutdown/status methods.
4. Simplify routing-only branches in PowerTransitionCoordinator while retaining real power lifecycle.
5. Make frontend routing-status compatibility explicitly return RoutingRuntimeStatusSnapshot.Unavailable.
6. Remove dormant Game Bar foreground watcher/delivery wiring.
7. Delete AddonRoutingRuntime + RoutingPipeline* graph.
8. Move RoutingOperationalState to the retained status-snapshot file.
9. Delete IHandheldRoutingComposition / factory / MsiClawRoutingComposition.
10. Delete route-scoped MSI stage/session wrappers.
11. Delete CenterMMainUiRoutingGuardStage wrapper only; leave Cleanup-B subsystem intact.
12. Delete WinGProtectionRoutingStage and CanonicalSteamDeckOutputStage.
13. Delete/migrate architecture-only tests.
14. Run reference-closure searches.
15. Build/test full solution and review the final diff for accidental current-owner changes.
```

Prefer direct deletion over deprecation attributes or `#if LEGACY_ROUTING` blocks.

---

# 22. Validation

Required:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug
dotnet build SteamInputAddonforClaw.slnx -c Release
dotnet test SteamInputAddonforClaw.slnx -c Release
git diff --check
```

Also run focused searches conceptually equivalent to:

```text
git grep -n "AddonRoutingRuntime" -- src tests
git grep -n "RoutingPipelineRuntimeCoordinator" -- src tests
git grep -n "IHandheldRoutingComposition" -- src tests
git grep -n "MsiClawRoutingComposition" -- src tests
git grep -n "WinGProtectionRoutingStage" -- src tests
git grep -n "CanonicalSteamDeckOutputStage" -- src tests
git grep -n "GameBarForegroundWatcher" -- src tests
git grep -n "HandleGameBarForegroundChangedAsync" -- src tests
```

Expected after intentional test/doc migration:

```text
no production source reference to deleted types
no test fixture requiring deleted architecture
historical docs may still mention them
```

Then inspect the final diff specifically for accidental modifications to:

```text
MsiClawAddonPhysicalOwnership
AddonControllerHidHideBaseline
MsiClawAddonPresentation
CanonicalViiperRuntime
MsiClawFrontButtonRuntime
WinGSuppressionGuard
CenterMRebootAuthorityTransition
StockCenterMStartupBaseline
owned-controller recovery / Device Arrival path
```

Those current owners should change only where a compile-time signature cleanup is mechanically required, not semantically.

---

# 23. Hardware smoke test

On MSI Claw, after automated tests pass:

## Stock authority

```text
1. Enable MSI Center M + restart.
2. Confirm physical controller is PID1901.
3. Launch/exit Steam game and enter/exit BPM.
4. Confirm no PID1902 takeover and no Addon virtual presentation appears.
```

## Addon authority

```text
1. Disable MSI Center M + restart.
2. Confirm PID1902 physical ownership + DirectInput + HidHide baseline commit.
3. Confirm initial Xbox360 presentation.
4. Enter BPM / start a Steam game → SteamDeck.
5. Exit BPM/game → Xbox360.
6. Confirm WING never surfaces native Xbox Game Bar while Addon authority is active.
7. Confirm OEM1/WING front-button runtime still functions according to current mappings.
8. Show/hide Overlay and confirm suppression/presentation ownership remains stable.
```

## Lifecycle

```text
1. Sleep/resume once under Addon authority.
2. Verify the controller converges safely without stock takeover.
3. Exercise a normal supported physical re-enumeration/recovery path if available.
4. Enable Center M + Restart and verify stock PID1901 restoration.
```

Do not manufacture pathological timing races solely for this cleanup.

---

# 24. Completion criteria

Cleanup A is complete when:

- `AddonRoutingRuntime` is deleted;
- the legacy `RoutingPipeline*` ownership graph is deleted;
- `IHandheldRoutingComposition` / factory / `MsiClawRoutingComposition` are deleted;
- legacy route-scoped MSI stage/session wrappers are deleted;
- `CenterMMainUiRoutingGuardStage` wrapper is deleted while the Cleanup-B Center M helper subsystem remains untouched;
- `CanonicalSteamDeckOutputStage` is deleted;
- `WinGProtectionRoutingStage` is deleted;
- the dormant Game Bar foreground presentation watcher/delivery path is deleted;
- `AddonRuntimeHost` no longer accepts or models an optional legacy routing backend;
- power orchestration no longer carries residual/preserved legacy-route branches;
- current frontend/status compatibility still receives `RoutingRuntimeStatusSnapshot.Unavailable`;
- `RoutingEligibilityPolicy`, `RoutingRuntimeStatusSnapshot`, and `RoutingTraceContext` remain for their current consumers;
- `RoutingOperationalState` is retained only as the small status compatibility enum, not by keeping the deleted session coordinator;
- current Full1902 physical/presentation/recovery/Win+G owners remain unchanged in authority semantics;
- Center M Enabled Steam/BPM cannot mutate physical controller authority;
- Center M Disabled Full1902 lifecycle still passes all current regression tests;
- Debug build passes;
- Release build passes;
- full Release test suite passes;
- `git diff --check` passes;
- reference-closure search finds no production use of deleted types.

The desired architectural result is simple:

```text
one controller authority decision: Center M Enabled vs Disabled
one Full1902 physical owner when Disabled
one Full1902 presentation owner when Disabled
one front-button feature owner
one Win+G suppression primitive/lifetime
one stock restoration path

and no dormant Steam-session physical-routing authority left beside them.
```
