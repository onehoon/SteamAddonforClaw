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

The application is pre-release. Do not preserve an obsolete production architecture only for backward source compatibility or developer-only tooling.

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

Cleanup A must make the source tree and user-visible Status UI match that already-shipped architecture.

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
Status UI no longer shows the obsolete legacy Controller Status card
legacy routing frontend/status contracts are deleted when they no longer serve current Full1902 production behavior
```

Developer-only vibration functionality is not a compatibility requirement for this cleanup. If legacy routing deletion makes it unavailable, that is acceptable. Do not keep production legacy code for it.

---

# 2. Current production proof — why this cleanup is now safe

Current production calls `steamRuntime.StartActualObservation()` but no longer creates `AddonRoutingRuntime`; the old runtime is hard-disabled with `AddonRoutingRuntime? routingRuntime = null;`.

Full1902 controller ownership is already held by the current owners:

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

Game Bar foreground presentation routing is already disconnected in production. `AddonProcessHost` still contains `GameBarForegroundWatcher`, `GameBarForegroundPresentationDelivery`, and callbacks into the legacy runtime, but the watcher is not started/subscribed. Delete this dormant graph.

`AddonRuntimeHost` still models an optional legacy backend that production can never have. Remove that optional-backend model instead of replacing it with another no-op wrapper.

---

# 3. Scope

## In scope

Remove:

1. legacy generic routing authority/orchestration;
2. legacy MSI routing composition and route-scoped wrappers;
3. legacy route-owned SteamDeck output wrapper;
4. legacy route-owned Win+G stage;
5. dormant Game Bar foreground presentation selection;
6. dead routing branches from `AddonRuntimeHost` / power orchestration;
7. the legacy Status-page Controller Status card and its presentation logic;
8. legacy routing frontend/status contracts that exist only for removed pre-Full1902 behavior;
9. legacy developer vibration-test routing dependencies if they block removal of the old production graph;
10. tests that exclusively verify deleted architecture or deleted Status-card presentation;
11. obsolete production parameters/callbacks whose only consumer was that architecture.

## Out of scope — Cleanup B

Do not delete the entire old Center M dummy/helper suppression subsystem here. Cleanup B remains for:

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
```

Exception: delete `CenterMMainUiRoutingGuardStage.cs` in Cleanup A because it is only an `IRoutingPipelineStage` wrapper.

## Developer vibration test policy

The developer vibration test may be broken, disabled, or temporarily removed by this cleanup.

Do not:

- keep `AddonRoutingRuntime` for it;
- keep `CanonicalSteamDeckOutputStage` for it;
- keep legacy routing status/frontend contracts solely for it;
- add a Full1902 rumble implementation here;
- add a new feedback manager/service/wrapper.

A later dedicated PR may redesign developer vibration against Full1902 if still useful.

---

# 4. Delete the legacy `Routing` authority graph

Delete after closing references:

```text
src/SteamInputAddonforClaw/Routing/AddonRoutingRuntime.cs
src/SteamInputAddonforClaw/Routing/IRoutingSafetySession.cs
src/SteamInputAddonforClaw/Routing/RoutingPipelineExecution.cs
src/SteamInputAddonforClaw/Routing/RoutingPipelinePlan.cs
src/SteamInputAddonforClaw/Routing/RoutingPipelineRuntimeCoordinator.cs
src/SteamInputAddonforClaw/Routing/RoutingPipelineSessionCoordinator.cs
src/SteamInputAddonforClaw/Routing/RoutingReconcileStatusRefresh.cs
```

This removes old concepts including:

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

Do not rename/recreate these concepts under new abstractions.

---

# 5. Remove the legacy Status-page Controller Status card

The current Status UI derives controller state from old routing fields:

```text
Routing.Available
Routing.OperationalState
Routing.SteamOutputActive
Routing.NativeDirectInputActive
```

and formats legacy states such as:

```text
Steam Controller (DInput)
MSI Center M Native (XInput)
```

through `StatusPresentation.FormatControllerStatus(...)`.

Delete the existing Controller Status card/row and presentation code used only by it, including after reference closure:

```text
StatusPresentation.FormatControllerStatus(...)
StatusPresentation.IsControllerStateTrusted(...)
controller-status-only XAML/name bindings
controller-status-only unit tests
```

Do not replace it with a new Full1902 status card in this PR.

Keep PR #475's Full1902-aware Addon operational status unchanged.

---

# 6. Remove legacy frontend/status routing contracts where dead

After removing the Controller Status card and allowing developer vibration functionality to disappear, perform a full non-test reference check for:

```text
RoutingRuntimeStatusSnapshot
RoutingOperationalState
FrontendRoutingSnapshot
FrontendRoutingOperationalState
FrontendRoutingEligibilityReason
RoutingEligibilityPolicy
RoutingDecision / routing-related frontend mapper fields
InProcessAddonFrontendControl._captureRoutingStatus
AddonRuntimeHost.CaptureRoutingStatus()
```

Delete any of these whose remaining role is only historical pre-Full1902 routing/status behavior.

Do not preserve compatibility DTOs because tests or developer-only UI still expect them. Update/delete those call sites instead.

If one still has a concrete current production consumer unrelated to deleted legacy behavior, preserve only that concrete use and document it in the PR.

Do not reinterpret old routing fields as Full1902 authority.

---

# 7. Delete legacy handheld routing composition

Delete:

```text
src/SteamInputAddonforClaw/Devices/Abstractions/IHandheldRoutingComposition.cs
src/SteamInputAddonforClaw/Devices/HandheldRoutingCompositionFactory.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawRoutingComposition.cs
```

Do not replace them with a new Full1902 composition/facade/manager. Full1902 already has explicit owners.

---

# 8. Delete route-scoped MSI stage/session wrappers

Delete:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawNativeModeSessionCoordinator.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawNativeModeStage.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawHidHideBaselineStage.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawPhysicalInputStage.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawPhysicalIsolationStage.cs
src/SteamInputAddonforClaw/CenterM/CenterMMainUiRoutingGuardStage.cs
```

Preserve active low-level/current Full1902 primitives:

```text
MsiClawDeviceAdapter
MsiClawNativeStateManager
MsiClawModeController / mode writer/resolver
MsiClawInputSource
MsiClawInputContracts
DirectInput enumerator/topology primitives
MsiClawAddonPhysicalOwnership
AddonControllerHidHideBaseline
```

Do not delete reusable rumble transport/endpoint primitives merely because the old route graph used them. But do not keep the old route graph for developer vibration.

---

# 9. Delete the old virtual-output routing stage

Delete:

```text
src/SteamInputAddonforClaw/VirtualOutput/Viiper/CanonicalSteamDeckOutputStage.cs
```

Current presentation authority is:

```text
MsiClawAddonPresentation
→ CanonicalViiperRuntime
→ persistent X360 + SteamDeck logical devices
→ exactly one attached/live presentation
→ CanonicalXbox360InputPublisher or CanonicalSteamDeckInputPublisher
```

Preserve the current VIIPER/session/publisher primitives and `SteamDeckSystemButtonOverlay`.

Do not preserve `CanonicalSteamDeckOutputStage` for developer vibration.

---

# 10. Delete legacy route-owned Win+G stage

Delete:

```text
src/SteamInputAddonforClaw/GameBar/WinGProtectionRoutingStage.cs
```

Keep:

```text
src/SteamInputAddonforClaw/GameBar/WinGSuppressionGuard.cs
```

Preserve Policy-B lifecycle exactly: one hook installed from the pumping message loop; Center M Disabled arms/proves it before first live presentation; no re-arm/disarm on presentation switching or ordinary recovery.

---

# 11. Delete dormant Game Bar foreground presentation path

Delete:

```text
GameBarForegroundWatcher
GameBarForegroundPresentationDelivery
AddonProcessHost._gameBarForegroundWatcher
AddonProcessHost._gameBarDelivery
OnGameBarForegroundChanged(...)
RequestGameBarPresentationReconcile(...)
AddonRuntimeHost.HandleGameBarForegroundChangedAsync(...)
associated shutdown/dispose code
```

Also delete tests whose sole purpose is the old foreground-delivery/presentation path.

Game Bar foreground state is not a Full1902 presentation input.

---

# 12. Simplify `AddonRuntimeCompositionFactory`

Remove:

```csharp
AddonRoutingRuntime? routingRuntime = null;
```

and remove the routing-runtime argument from `AddonRuntimeHost` construction.

Remove obsolete parameters/imports/callbacks that exist only for the old backend.

Preserve:

```text
SteamSessionRuntime
StartActualObservation()
raw BPM presentation snapshot/events
recovery safety state where still used by current power/status behavior
stockCenterMAuthority
stock PID1901 resume baseline callback
WinG suppression policy wiring
Full1902 AddonStatus capture from PR #475
```

Do not restart `StartRoutingObservation()`.

---

# 13. Simplify `AddonRuntimeHost`

Remove the optional legacy backend and behavior that only delegated to it:

```text
AddonRoutingRuntime? _routingRuntime
legacy routing participant registration
legacy auxiliary routing power participants
legacy normal ReconcileAsync path
legacy fresh routing reconcile on resume
legacy preserved-route resume reconcile
legacy residual-route cleanup retry
legacy routing cancel-in-flight callback
legacy routing shutdown/dispose override machinery
legacy routing-specific termination snapshot inputs
legacy Game Bar foreground route handler
legacy developer vibration routing methods/callbacks
```

Delete obsolete call sites instead of creating no-op replacements.

Preserve current duties:

```text
SteamSessionRuntime ownership
ActualRunningAppId publication
BPM event forwarding / presentation snapshot access
power notification ownership
stock PID1901 resume baseline for Center M Enabled
RecoverySafetyState integration still used by current process
PowerResumeObserved
shutdown-safe Steam observation teardown
```

Simplify current power wiring only as necessary to remove dead routing callbacks. Do not redesign the generic power primitive and do not move Full1902 PnP/DirectInput recovery into it.

---

# 14. User-termination safety

Do not preserve fictional routing transition/cleanup state solely for the old `UserTerminationGuard` shape.

Preserve real Full1902 protections:

```text
Center M Disabled
→ Runtime mandatory
→ ordinary Exit blocked

Disabled controller startup committing
→ transition/exit blocked

live owned recovery mutation where current safety contract requires it
→ fail closed

Enable Center M + Restart / uninstall
→ existing verified stock-safe authority transition remains authoritative
```

Remove dead routing-only termination inputs/reasons/tests where isolated. Do not weaken current Full1902 mandatory-runtime safety and do not add a new termination manager.

---

# 15. Tests

Delete tests that exclusively exercise deleted architecture, including after fresh reference closure:

```text
AddonRoutingRuntimeTests.cs
RoutingPipelineRuntimeCoordinatorTests.cs
RoutingReconcileStatusRefreshTests.cs
HandheldRoutingCompositionFactoryTests.cs
MsiClawRoutingCompositionTests.cs
MsiClawRoutingCompositionOem1ActionPathTests.cs
legacy MsiClawNativeModeSessionCoordinator tests
legacy MsiClawPhysicalInputStage tests
legacy MsiClawPhysicalIsolationStage tests
legacy HidHide routing-stage tests
WinGProtectionRoutingStage tests
GameBarForegroundWatcher/delivery tests for old presentation routing
legacy CanonicalSteamDeckOutputStage tests not covering shared primitives
legacy Controller Status presentation tests
legacy developer vibration tests requiring deleted routing runtime/output stage
```

Preserve/update current tests for:

```text
MsiClawNativeStateManager
MsiClawInputSource
AddonControllerHidHideBaseline
MsiClawAddonPhysicalOwnership
MsiClawAddonPresentation
CanonicalViiperRuntime
CanonicalSteamDeckInputPublisher
CanonicalXbox360InputPublisher
SteamDeckSystemButtonOverlay
MsiClawFrontButtonRuntime
WinGSuppressionGuard
PR8/PR9/PR10 Full1902 recovery
CenterMRebootAuthorityTransition
PR12 stock-safe uninstall
PR #475 Full1902 AddonStatus
```

Required regression coverage:

- production composition has no legacy routing owner;
- ActualRunningAppId observation remains;
- BPM forwarding/presentation snapshot remains;
- Center M Enabled resume still uses stock PID1901 baseline;
- Center M Disabled does not use stock PID1901 baseline;
- Full1902 physical ownership/presentation/recovery remains intact;
- WinG suppression and front-button Full1902 pulse path remain intact;
- Status UI no longer renders legacy Controller Status;
- Game Bar foreground cannot select a presentation.

---

# 16. Reference-closure rule

Before deleting each file classify every current non-test reference:

```text
A. legacy routing graph
→ delete

B. current Full1902 / production primitive
→ keep primitive, delete old wrapper

C. developer/status compatibility only
→ if no current Full1902 production behavior depends on it, delete
```

Examples:

```text
MsiClawNativeStateManager → keep
MsiClawPhysicalInputStage → delete
RoutingRuntimeStatusSnapshot → delete if no current production consumer remains after status/dev cleanup
RoutingTraceContext → keep if current DirectInput/VIIPER diagnostics still use it
WinGSuppressionGuard → keep
WinGProtectionRoutingStage → delete
```

Do not delete by filename alone.

---

# 17. Documentation cleanup

Historical `docs/work-order` files may remain as history.

Update living docs that make present-tense claims that the deleted route stack is current, including as applicable:

```text
docs/VIIPER_INTEGRATION.md
docs/VIIPER_MIGRATION_TODO.md
docs/OEM1_CenterM_Runtime_Foundation_Status.md
README/current architecture docs outside historical work orders
```

Do not churn historical completed work orders solely to remove old class names.

---

# 18. Lifecycle / overengineering constraints

Preserve real supported lifecycle safety:

```text
Sleep / Hibernate / Resume
Restart / Crash / Shutdown
physical device loss / PnP re-enumeration
PID1901 ↔ PID1902 restoration/reclaim
HidHide ownership/cleanup
VIIPER ownership/teardown
Center M Disabled mandatory Runtime lifetime
Enable Center M + Restart stock-safe release
stock-safe uninstall preparation
actual native / DirectInput / HidHide / VIIPER operation failure
```

Do not add locks/epochs/barriers/managers for theoretical instruction-level races.

Do not replace deleted code with a new routing facade, controller-authority coordinator, compatibility manager, migration service, new shutdown manager, new status manager, or new rumble manager.

Target:

> one clear Full1902 physical owner, one presentation owner, one recovery path, one stock-release path, and no dead second routing authority in the tree.

---

# 19. Completion criteria

```text
[ ] AddonRoutingRuntime deleted.
[ ] RoutingPipeline controller-authority orchestration deleted.
[ ] IHandheldRoutingComposition / HandheldRoutingCompositionFactory deleted.
[ ] MsiClawRoutingComposition deleted.
[ ] route-scoped MSI native/input/HidHide wrappers deleted.
[ ] CanonicalSteamDeckOutputStage deleted.
[ ] WinGProtectionRoutingStage deleted.
[ ] dormant Game Bar foreground presentation path deleted.
[ ] AddonRuntimeCompositionFactory has no always-null routingRuntime variable.
[ ] AddonRuntimeHost has no optional legacy routing backend model.
[ ] Status UI no longer renders legacy Controller Status card.
[ ] dead legacy routing frontend/status contracts deleted.
[ ] developer vibration functionality was not used to preserve legacy production code.
[ ] Steam Actual AppID/BPM observation still works.
[ ] Center M Enabled stock PID1901 resume baseline still works.
[ ] Center M Disabled does not regain stock-baseline mutation.
[ ] Full1902 physical ownership/presentation/recovery tests pass.
[ ] WinG suppression / front-button Full1902 path tests pass.
[ ] no new authority/facade/manager abstraction introduced.
[ ] no current low-level primitive deleted only because a legacy wrapper once used it.
[ ] build passes.
[ ] production-relevant test suite passes.
```

Suggested PR title:

```text
Full1902 Cleanup A: remove legacy Steam-session routing authority and status UI
```
