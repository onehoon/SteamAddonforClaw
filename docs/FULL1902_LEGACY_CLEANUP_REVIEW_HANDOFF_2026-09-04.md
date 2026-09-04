# Full1902 Legacy Cleanup Review — Handoff

> Date: 2026-09-04  
> Repository: `onehoon/SteamAddonforClaw`  
> Production-code review baseline: `main` at `aa77801c8dfcb621b16b854f928fc8aefd538e89` (after Cleanup B / PR #477)  
> Purpose: conversation handoff / follow-up cleanup planning  
> This is **not** a work order and does not override the canonical Full1902 documents.

## 2026-09-04 policy update

This revision records two explicit product/cleanup decisions made after the first version of this handoff.

### Decision A — Developer implementation may remain, but must not keep production legacy architecture alive

Developer-menu/test/diagnostic source does **not** need to be deleted as part of the production cleanup.

Preferred direction:

```text
Developer source / experimental diagnostic implementation
        X
        │ disconnect from production authority/runtime graph
        X
Full1902 production controller ownership / presentation / recovery / status
```

It is acceptable for a developer feature to remain temporarily unavailable or disconnected. Tests can be rebuilt later when the feature is redesigned for Full1902.

However:

> Do not preserve a legacy production DTO, state authority, routing model, recovery owner, or runtime abstraction solely because disconnected Developer code still references it.

If production cleanup breaks compilation in a Developer-only page/helper, make the smallest Developer-side adjustment needed to isolate/disable that feature. Do **not** recreate a generalized production abstraction just to keep an old diagnostic working.

### Decision B — Controller Software / ClawTweaks / HHC conflict detection is no longer product policy

Previous handoff text recommended preserving the `Controller Software` card and ClawTweaks/HHC conflict detection. That recommendation is now **superseded**.

Current product policy:

```text
Center M Disabled
→ Addon is the controller authority
→ Addon does not detect/arbitrate/coexist with third-party controller managers
→ ClawTweaks/HHC presence or runtime state is not an authority input
```

The Addon must react to **actual controller/lifecycle facts** instead:

```text
owned PID1902 → PID1901 drift
→ reclaim/reconcile according to current Full1902 ownership policy

physical input disappears / PnP re-enumerates
→ current physical recovery path

HidHide mutation/readback fails
→ fail closed

VIIPER attach/detach/teardown fails
→ fail closed / current pending-cleanup policy
```

The Addon does not need to identify which third-party application caused a physical or configuration change.

Therefore the `Controller Software` UI card, ClawTweaks/HHC detection/probes, manager classification, and manager-based admission/compatibility logic are now cleanup targets.

The one Center M fact that **must remain** is the startup-root authority state:

```text
MSI_Center_M_Server task
MSI_Center_M_Updater task
MSI Foundation Service startup mode
```

That startup-root state, not Center M process detection, is the Full1902 authority source.

---

## 1. Canonical authority to preserve

Before any follow-up cleanup, read the Full1902 documents together using the authority order in:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`

The current product has exactly two controller-authority modes:

```text
Center M Enabled
→ MSI / stock controller authority
→ desired physical PID = PID1901

Center M Disabled
→ Addon Runtime controller authority
→ desired physical PID = PID1902
→ Addon Runtime mandatory
→ persistent Addon HidHide authority
```

Steam/BPM selects only the Addon-owned virtual presentation:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

Cleanup must not reintroduce a third authority source, route-level ownership, a second VIIPER owner, or separate recovery authority.

The current HidHide policy is deterministic normalization, not historical multi-owner restoration:

```text
Center M Disabled
→ normalize Applications to verified HidHideCLI + HidHideClient + current Addon executable
→ normalize Hidden Devices to zero targets before exact ownership, then exact owned PID1902 primary collection only
→ verify by readback

Center M Enabled
→ release Addon-owned current controller state
→ preserve official HidHide CLI/Client registrations
→ do not reconstruct historical third-party HidHide state
```

---

## 2. Why another cleanup pass is justified

Cleanup A and Cleanup B removed the large legacy Steam-routing owner graph and the Center M dummy/MainUI suppression subsystem, but the current source still contains old routing-era contracts around the new Full1902 owners.

The remaining pattern is roughly:

```text
current Full1902 physical/presentation/lifecycle owners
        +
old routing-era startup/recovery/status shell
        +
old controller-manager compatibility shell
        +
Developer features still referencing some old contracts
```

The objective is not LOC reduction for its own sake. The objective is:

> one clear owner / one authority model / one teardown path / one failure policy, while preserving real handheld lifecycle safety.

Do not add state, locks, epochs, barriers, wrappers, managers, or generalized abstractions to replace code being deleted unless a real supported lifecycle requires them.

---

# 3. Confirmed cleanup candidates

## 3.1 PowerTransitionCoordinator still contains dead legacy routing branches

`PowerTransitionCoordinator` still has routing-era fields/callbacks such as:

```text
_hasResidualRoutingCleanup
_retryResidualRoutingCleanup
_hasPreservedRoutingSession
_reconcilePreservedRoutingSession
_afterPreservedRecoveryCommit
```

The Resume path still contains complete branches for:

```text
preserved routing session reconciliation
residual routing cleanup retry
deferred routing reconciliation after recovery commit
```

Current production composition does not provide these callbacks, so they fall back to inactive defaults.

### Cleanup direction

Remove only the dead routing-specific branches, callbacks, logs, and tests.

Preserve all real power/lifecycle safety:

```text
PowerMutationGate
PowerTransitionWatcher
suspend barrier
resume epoch validation
participant quiesce deadline
RecoverySafetyState while it still has a real production caller
stock baseline resume
current Full1902 post-resume controller recovery
```

This is the best first cleanup because it is mechanically isolated and does not require a product decision.

---

## 3.2 Controller Software / third-party manager subsystem is now a full cleanup target

At the reviewed baseline, production startup intentionally constructs controller software providers for MSI Center M, ClawTweaks, and Handheld Companion and feeds them into `ControllerEnvironmentAssessmentProvider` / `ControllerManagerClassification`.

That was previously used as a Disabled-boot admission gate:

```text
ClawTweaks/HHC detected
→ ControllerManager != None
→ Full1902 admission blocked
```

That behavior is no longer desired.

### New product invariant

```text
Center M Disabled
→ Addon authority is selected by Center M startup roots
→ third-party controller-manager process/install state is not consulted
→ no coexistence/arbitration layer
```

The cleanup should remove the complete production reference closure for controller-software conflict detection where no unrelated feature needs it.

Expected target set includes, subject to fresh reference closure:

```text
Status UI
- Controller Software SettingsExpander/card
- ControllerSoftwareExpander rendering
- controller-software status formatting

Frontend/status contracts
- Frontend controller-software snapshots/status enums if no other consumer remains
- ControllerSoftwareStatus
- ControllerSoftwareKind
- IControllerSoftwareStatusProvider
- ControllerSoftwareStatusSorter / formatter

ClawTweaks
- ClawTweaksSoftwareStatusProvider
- ClawTweaksInstallationProbe
- ClawTweaksRuntimeDetector
- obsolete ClawTweaks environment detector/mapping
- dedicated probe interfaces/types used only by this subsystem

Handheld Companion
- HandheldCompanionSoftwareStatusProvider
- HandheldCompanionInstallationProbe
- HandheldCompanionRuntimeDetector
- dedicated detection/probe code used only by this subsystem

Manager classification
- ControllerManagerClassification
- ControllerManagerKind
- ControllerManagerClassifier
- manager-based compatibility reasons
- manager-based admission tests

Disabled boot
- controller-manager assessment dependency
- `manager != None` admission block
```

### MSI Center M distinction

Do not confuse MSI Center M **software/process detection** with Full1902 **startup-root authority**.

Likely removable if no unrelated consumer remains:

```text
MsiCenterMSoftwareStatusProvider
MsiCenterMInstallationProbe
MsiCenterMRuntimeDetector
Center M process-running / installation status used only by old compatibility/status
```

Must remain:

```text
CenterMStartupControl
Center M Server task state
Center M Updater task state
MSI Foundation Service startup mode
CenterMStartupHelper
CenterMRebootAuthorityTransition
StockCenterMStartupBaseline
```

### No replacement third-party detector

Do not replace the deleted ClawTweaks/HHC detection with a more generic process detector, ownership inspector, arbitration service, or compatibility manager.

Actual lifecycle failures are already observable at the physical/HidHide/VIIPER layers and should be handled there.

---

## 3.3 Startup ControllerEnvironmentMode is a legacy authority abstraction

The current `StartupControllerEnvironmentMapper.Map(...)` ignores its assessment input and always returns:

```text
ControllerEnvironmentMode.StockCenterM
```

The old enum still carries modes such as:

```text
Unsupported
StockCenterM
ClawTweaks
HHCManaged
Indeterminate
```

That model predates Full1902 authority and becomes even less useful once the Controller Software subsystem is removed.

Current startup authority is:

```text
supported MSI Claw?
        ↓
Center M startup roots
        ↓
Enabled  → stock authority
Disabled → Addon authority
Partial / unavailable → fail closed / passive
```

### Related legacy field

`StartupResult.LegacyRoutingAllowed` is also a legacy contract. The useful fact it approximates is already represented more accurately downstream as:

```text
stockCenterMAuthority
```

### Cleanup direction

After Controller Software removal, simplify/remove the reference closure around:

```text
ControllerEnvironmentMode
StartupControllerEnvironmentMapper
LegacyRoutingAllowed
mode-dependent startup branches that cannot occur in current production
old ControllerEnvironment compatibility projection
```

Keep real controller-topology stabilization. Do not weaken:

```text
stable snapshot requirement
bounded timeout
MSI Claw internal topology recognition
PID1901/PID1902 control-HID readiness
PnP re-enumeration handling
```

`ControllerEnvironmentWaiter` may be reduced to topology readiness rather than environment-mode readiness if fresh reference closure supports it.

---

## 3.4 Environment Discovery is Developer-only: keep source, disconnect it from production cleanup constraints

The Developer `Environment Discovery` implementation currently carries old concepts such as software detection / controller environment reporting.

Previous recommendation was to migrate the report to Full1902 controller-manager facts. That recommendation is now superseded because controller-manager detection itself is being removed from production policy.

### Current direction

Do **not** spend production cleanup effort preserving or redesigning Environment Discovery right now.

Preferred handling:

```text
keep Developer Environment Discovery source if desired
→ disconnect it from production runtime/status/authority dependencies
→ allow it to be temporarily unavailable or stale as a developer-only tool
→ redesign/reconnect later if needed
```

If deletion of a production contract breaks this Developer code, make the smallest Developer-side compile fix or disable its invocation. Do not keep production controller-manager/status abstractions for the report.

Future Environment Discovery, if reintroduced, should report direct Full1902 facts rather than recreate a controller-manager authority model.

---

## 3.5 M1M2DiagnosticCoordinator is dead production code

`Diagnostics/M1M2DiagnosticCoordinator.cs` remains in the tree, but current production/UI composition does not create it.

It still references old RecoveryManager mutation/lease APIs and therefore helps keep the routing-era recovery model alive.

### Cleanup direction

Because the new Developer policy allows diagnostic source to remain disconnected, there are two acceptable outcomes:

1. delete `M1M2DiagnosticCoordinator` if it blocks production Recovery cleanup and has no useful future value; or
2. keep the source but sever its dependency on production Recovery ownership APIs / production composition.

Do not preserve RecoveryManager mutation/lease architecture solely for this diagnostic.

Future M1/M2 remapping should be designed on current Full1902 input/presentation ownership.

---

## 3.6 StartupVirtualOutputRecoveryInspector is a legacy recovery leaf

`StartupVirtualOutputRecoveryInspector` and `IStartupVirtualOutputRecoveryInspector` remain, but current production startup composition does not wire them.

They operate on old `AddonOwnedVirtualDeviceRecoveryEntry` journal state.

### Cleanup direction

Delete the inspector/interface/tests if fresh grep confirms no production call path.

Do not replace them with a new startup virtual-device inspector unless current Full1902 lifecycle code actually needs one. Current presentation ownership already has its own VIIPER lifecycle/teardown policy.

---

## 3.7 AddonOwnedVirtualDeviceTracker is another legacy recovery/classifier seam

`VirtualOutput/Viiper/AddonOwnedVirtualDeviceTracker.cs` still implements the old identity-exclusion seam but current production composition does not appear to create it.

### Cleanup direction

If no live Full1902 owner feeds this tracker, delete it and simplify the classifier/reference closure.

The previous warning about preserving it for third-party controller-manager conflict detection is now superseded because that conflict-detection subsystem itself is being removed.

Do preserve any virtual-device identification that is independently required by current physical ownership, PnP recovery, VIIPER teardown, or uninstall cleanup.

---

# 4. RecoveryJournal is the largest remaining architectural cleanup candidate

The current journal still represents old mutation ownership such as:

```text
DeviceNativeStateChanged
HidHideDeviceAdditions
ExecutableWhitelistAdditions
AddonOwnedVirtualDeviceEntries
OriginalHidHideActiveState
```

That model was designed around:

```text
capture previous state
record every owned mutation
crash/restart
replay/rollback only recorded mutations
restore earlier global state
```

Current Full1902 HidHide/authority policy is different:

```text
selected current authority
→ normalize to deterministic current baseline
→ readback verify
→ fail closed on actual operation failure
```

The Addon explicitly does not maintain arbitrary third-party HidHide backup/restore ownership.

At the reviewed baseline, current physical/presentation owners do not appear to create new routing-era RecoveryJournal mutation sessions. Many older consumers still read the journal and therefore keep the architecture alive.

Observed dependent areas include:

```text
StartupCoordinator stale recovery path
Disabled boot admission/recovery checks
StartupHidHideRecoveryCleaner
PowerTransitionCoordinator incomplete-recovery state
UserTerminationGuard recovery-mutation reason
MachineRecoverySafetyInspector
some prerequisite/setup gating
uninstall/startup compatibility paths
legacy recovery tests / developer diagnostics
```

This requires a dedicated reference-closure pass before deletion.

---

## 4.1 Product decision still pending: old recovery.json compatibility

One decision remains before the major RecoveryJournal cleanup.

### Option A — drop old development-build recovery.json compatibility

Best fit for the current pre-release development stage if acceptable.

Then remove the routing-era recovery architecture rather than preserving it for old test/dev state.

Potential target set after reference closure:

```text
RecoveryJournal routing-era mutation schema
RecoveryManager mutation/lease APIs
StartupHidHideRecoveryCleaner journal-specific behavior
StartupVirtualOutputRecoveryInspector
MachineRecoverySafetyInspector journal scan
journal-derived power/termination gates
related setup/status plumbing
legacy recovery tests
```

### Option B — keep only a one-shot retirement shim

If older development builds with `recovery.json` must be handled, do not preserve the full replay/restore architecture.

Prefer a bounded compatibility path:

```text
old recovery.json found
→ determine current Full1902 authority
→ normalize current HidHide/controller state using current policy
→ verify current safe state
→ delete old recovery.json
```

No historical arbitrary third-party restoration, no mutation lease manager, no generalized replay state machine.

### Current recommendation

If development compatibility can be dropped, choose Option A.

---

# 5. DisabledBootControllerAdmission should simplify significantly

At the reviewed baseline, Disabled boot admission includes roughly:

```text
1. controller-manager assessment
2. runtime prerequisites
3. stale RecoveryJournal check
4. deterministic HidHide baseline normalization/readback
```

With the new Controller Software policy, step 1 is obsolete.

After later RecoveryJournal cleanup, step 3 may also disappear.

The desired eventual admission contract is approximately:

```text
Center M startup roots == Disabled
+ supported hardware
+ stable physical topology
+ required runtime prerequisites Ready
+ deterministic HidHide baseline normalized and verified
= Full1902 Addon admission may proceed
```

This is not a request to weaken fail-close behavior. Actual prerequisite inspection failure, HidHide operation failure, ambiguous physical identity, or unsafe VIIPER state must still block live presentation.

---

# 6. MachineRecoverySafetyInspector is likely removable with RecoveryJournal

The current prerequisite-install safety gate still checks recovery state through machine/profile-oriented recovery inspection.

Supported product scope is:

```text
1 Windows user
1 interactive session
Fast User Switching unsupported
RDP/multi-session unsupported
```

If RecoveryJournal is no longer a current production mutation authority, scanning other profiles for historical journal state becomes both unnecessary and outside the supported lifecycle model.

### Cleanup direction

Keep HidHide/usbip-win2 prerequisite install/verification itself.

Remove obsolete journal/profile recovery gating after the RecoveryJournal decision.

Do not weaken actual package/install-operation verification or fail-close behavior.

---

# 7. UserTerminationGuard can simplify after RecoveryJournal cleanup

Current termination safety includes concepts such as:

```text
RuntimeShuttingDown
RecoveryMutationOwned
ControllerAuthorityMandatory
```

`ControllerAuthorityMandatory` is a real Full1902 invariant and must remain:

```text
Center M Disabled
→ Addon Runtime mandatory
→ ordinary user Exit must not terminate controller authority
→ supported release path is Enable Center M + Restart / uninstall policy
```

`RecoveryMutationOwned` should be re-evaluated after RecoveryJournal deletion. Do not keep it solely to preserve a dead journal contract.

There is also a historical enum name such as `RoutingTransition` whose current meaning is controller-authority transition, not Steam routing. If still used after cleanup, rename it to a current authority term rather than adding another state manager.

---

# 8. Developer Test Mode — keep implementation if desired, remove production authority/runtime coupling

The Developer UI currently exposes a synthetic Test Mode that historically treated Steam as active without a real game.

That synthetic effective session is not an authoritative Full1902 presentation input. Current production presentation should remain based on actual facts:

```text
actual RunningAppID
Big Picture state
```

### Revised direction

Do **not** require deletion of the Developer page/Test Mode source in the production cleanup.

Instead:

```text
Developer Test Mode source may remain
→ must not feed controller ownership
→ must not feed Full1902 X360/SteamDeck presentation selection
→ must not keep EffectiveSteamSessionSource / DeveloperTest production semantics alive
→ must not keep production routing/status/recovery abstractions alive
```

If the old UI becomes non-functional after this disconnect, that is acceptable for now. It can be rebuilt later as a true Full1902 test seam.

Tests for the old behavior may be removed/disabled if they force production legacy contracts to survive.

---

# 9. Developer Vibration Test — keep source if useful, remove production legacy bridge dependency

The Developer vibration UI currently reports unavailable because the old routing-owned transport was removed.

The important cleanup principle is now:

> Do not delete Developer source just to reduce files, but do not preserve old production feedback authority solely for that source.

Therefore:

```text
VibrationTestPage / developer helper source may remain
but
old production FeedbackAuthority / legacy bridge / routing-owned transport must not remain merely to support it
```

If `FeedbackAuthority`, `SteamDeckRumbleFeedbackBridge`, vibration-session RPC, or other old feedback plumbing has no real production owner, remove it from the production runtime reference closure even if that leaves the developer page disconnected/unavailable.

Useful RE-backed low-level MSI rumble primitives may remain if future Full1902 rumble work is near:

```text
MsiClawRumbleSink
MsiClawRumblePacketBuilder
TwoMotorRumble / physical write result primitives
```

Future production rumble should attach to current `MsiClawAddonPresentation` / VIIPER ownership, not resurrect the legacy routing feedback authority.

---

# 10. DiagnosticSessionTracker should not preserve synthetic production Steam state

`DiagnosticSessionTracker` still contains old effective-session concepts such as:

```text
RawRunningAppID
EffectiveRunningAppID
EffectiveSource
DeveloperTest / BigPicture synthetic identity
```

Developer diagnostic source may remain, but synthetic `DeveloperTest` production Steam-session state should not survive solely for diagnostics.

Simplify/remove only the obsolete effective-session coupling when production reference closure allows it.

Preserve useful physical input diagnostics such as:

```text
ControllerState change logging
DirectInput POV diagnostics
D-pad transition diagnostics
analog diagnostics
```

---

# 11. ControllerEnvironmentCompatibility is a stock-era status/setup contract and should be retired

`SystemStatusProvider` still computes the old controller-environment compatibility model underneath the newer Full1902 owner-aware status.

That old model is centered on MSI Center M installation/runtime/process state and third-party manager compatibility, which is now contrary to product policy.

Healthy Disabled mode should not be described as incompatible merely because Center M is intentionally not running.

### Target direction

Remove the old compatibility projection/reference closure rather than permanently carrying it under Full1902 status.

The eventual production status should derive from current facts such as:

```text
HardwareCompatibility
CenterMStartupState
PrerequisiteAssessment
Full1902 owner/presentation health
SteamPresentationSnapshot
actual recovery/lifecycle safety where still current
```

Do not create a replacement generalized `ControllerEnvironmentCompatibilityV2` abstraction.

---

## 11.1 Status UI: delete Controller Software card

The current Status UI contains:

```text
Device
Steam Game
Controller Software
Routing Components
InfoBar
```

The `Controller Software` expander/card should now be removed.

Do not replace it with third-party manager warnings.

The status page can continue to show direct Addon/current-component health. Cleanup of old compatibility warning conditions should happen with the frontend/status contract cleanup.

---

## 11.2 First-Time Setup must stop depending on old controller compatibility

`FirstTimeSetupPolicy` currently consumes old controller-environment compatibility and blocks on Unsupported/Indeterminate states.

That dependency should be removed as the compatibility model is retired.

Prerequisite setup should use current facts such as:

```text
supported hardware
current Center M startup authority state where relevant
actual prerequisite install/provisioning state
actual operation safety/failure
RecoveryJournal state only while that architecture still exists
```

It should not require Center M process-running status or absence of ClawTweaks/HHC.

---

# 12. Small dead diagnostics / compatibility leaves

A fresh production-reference pass should include:

```text
ClawTweaksCompatibilitySnapshotLogger
StartupVirtualOutputRecoveryInspector
AddonOwnedVirtualDeviceTracker
M1M2DiagnosticCoordinator production dependencies
```

With the new policy:

- ClawTweaks compatibility logging has no product role and can be deleted.
- third-party-manager-specific diagnostic/probe code should not be retained in production.
- Developer-only source may remain only if isolated from production authority/state contracts.

---

# 13. Do not delete current Full1902 lifecycle owners

The following are intentionally **not** cleanup targets merely because they are complex or old:

```text
CenterMStartupControl
SteamInputAddonforClaw.CenterMStartupHelper
StockCenterMStartupBaseline
CenterMRebootAuthorityTransition

MsiClawDeviceAdapter
MsiClawNativeStateManager / current native mode primitives
MsiClawAddonPhysicalOwnership
MsiClawInputSource / DirectInput primitives
AddonControllerHidHideBaseline
DisabledBootControllerAdmission after obsolete manager/recovery gates are removed

WindowsDeviceArrivalWatcher / current PnP recovery
PID1902 owned-drift reclaim

MsiClawAddonPresentation
CanonicalViiperRuntime
current VIIPER attach/detach/pending-cleanup mechanics
CanonicalSteamDeckInputPublisher
CanonicalXbox360InputPublisher
SteamDeckSystemButtonOverlay

MsiClawFrontButtonRuntime
WinGSuppressionGuard

PowerMutationGate
PowerTransitionWatcher
current suspend/resume barrier/epoch/deadline behavior

RoutingTraceContext where still used by current physical/VIIPER diagnostics
```

### Removed from the previous preserve list

The following were previously listed as preserve-by-default but are now cleanup targets:

```text
ControllerManagerClassification
current ClawTweaks/HHC conflict probes
Controller Software status providers/cards
Center M process/runtime compatibility detection used only by that subsystem
```

### VIIPER caution

VIIPER `PendingCleanup` state is not automatically legacy. Current detach/teardown failure recovery is a real lifecycle concern and must remain if used by the live presentation owner.

---

# 14. Revised recommended implementation order

## Cleanup C — dead Power routing branches

Scope:

```text
PowerTransitionCoordinator preserved-routing branch
PowerTransitionCoordinator residual-routing-cleanup branch
routing-only callbacks/fields/logs/tests
```

No product decision required.

---

## Cleanup D — remove Controller Software / third-party manager compatibility subsystem

This should now be the next major structural cleanup after C.

Scope after fresh reference closure:

```text
Status Controller Software card/expander
Frontend controller-software DTOs
ControllerSoftwareStatus providers/contracts/sorter/formatter
ClawTweaks install/runtime detection used by controller-manager compatibility
Handheld Companion install/runtime detection used by controller-manager compatibility
ControllerManagerClassification / classifier / reasons
DisabledBoot manager gate
manager-based compatibility logic/tests
MSI Center M software/process status detection if only used by this old compatibility subsystem
ClawTweaksCompatibilitySnapshotLogger
```

Preserve Center M startup-root authority and all actual lifecycle failure handling.

---

## Cleanup E — startup/status authority-contract simplification

After D removes the manager/software model, simplify:

```text
ControllerEnvironmentMode
StartupControllerEnvironmentMapper
LegacyRoutingAllowed
ControllerEnvironmentAssessmentProvider if no current non-manager use remains
ControllerEnvironmentCompatibility
mode-only startup branches/tests
frontend compatibility DTOs
Status warning dependency
FirstTimeSetup compatibility dependency
```

Preserve topology stabilization and direct current Full1902 facts.

Developer Environment Discovery is not a reason to keep these production abstractions; disconnect/minimally adjust the Developer feature instead.

---

## Cleanup F1 — dead recovery leaves

Scope:

```text
StartupVirtualOutputRecoveryInspector
AddonOwnedVirtualDeviceTracker if unreferenced by live Full1902 ownership
M1M2 diagnostic dependencies that keep recovery mutation APIs alive
other dead journal-owned helpers
related production tests
```

Developer source may remain if isolated.

---

## Cleanup F2 — RecoveryJournal architecture retirement

Prerequisite decision:

```text
drop old recovery.json compatibility
or
one-shot Full1902 normalization + retirement shim
```

Then remove old mutation/replay/lease architecture and dead consumers while preserving deterministic current ownership/teardown safety.

---

## Cleanup G — disconnect Developer legacy production couplings

This is **not** a Developer-source deletion PR.

Goal:

```text
Developer/Test Mode/Vibration/Environment Discovery source may remain
but
no Developer feature may require legacy production Steam-routing, feedback-authority,
controller-manager, status-compatibility, or recovery-mutation architecture
```

Likely production-side removals:

```text
synthetic DeveloperTest effective Steam state
obsolete production RPC/runtime hooks whose only caller is a disconnected Developer feature
legacy vibration feedback authority/bridge if no real production owner
legacy diagnostic session coupling to synthetic effective Steam state
```

If a Developer page must become unavailable until later redesign, that is acceptable.

---

# 15. Decisions to carry into the next conversation

The following are now settled:

1. **Developer code policy — settled**
   - keep Developer implementation/source where convenient;
   - disconnect it from production legacy architecture;
   - do not preserve production abstractions solely for Developer tools;
   - tests/features may be rebuilt later.

2. **Controller Software / third-party manager policy — settled**
   - delete the Status `Controller Software` card;
   - delete ClawTweaks/HHC controller-manager detection/probes/classification where only used for this policy;
   - no third-party controller-manager arbitration in Full1902;
   - Center M startup roots remain the only controller-authority selector.

3. **Environment Discovery — settled for cleanup purposes**
   - do not redesign it now;
   - keep source if desired, but disconnect/minimally disable rather than preserve production legacy contracts.

The following still needs a later decision:

4. **Recovery compatibility — still pending**
   - can old development-build `recovery.json` simply be unsupported/removed?
   - or is a one-shot retirement shim required?

5. **Low-level rumble primitives — likely preserve**
   - keep useful RE-backed MSI packet/sink primitives if production rumble work is near;
   - remove only legacy production feedback authority/bridge if unowned.

---

# 16. Review / work-order policy for follow-up

For each cleanup work order:

- re-read the latest Full1902 authority documents before implementation;
- inspect latest `main` because this area is changing rapidly;
- fresh-grep every proposed deletion before writing the final file list;
- delete reference closures rather than preserving dead DTOs/interfaces solely for tests or Developer features;
- keep real Sleep/Hibernate/Resume, Restart/Crash/Shutdown, PnP loss/re-enumeration, PID1901↔PID1902 restoration, HidHide ownership, VIIPER teardown, rollback/fail-close safety;
- do not defend theoretical instruction-level races by introducing new authority/state/manager layers;
- do not retain unsupported Fast User Switching/RDP/multi-session machinery;
- do not replace deleted ClawTweaks/HHC detection with a generic manager/arbitration abstraction;
- do not replace a deleted legacy abstraction with a new generalized abstraction unless an actual current production lifecycle requires it.

The target is:

```text
one current Full1902 controller authority model
one physical owner
one presentation owner
one deterministic HidHide policy
one clear teardown/recovery policy
no legacy Steam-routing authority shell
no third-party controller-manager arbitration shell
Developer tools isolated from production authority
```

---

# 17. Suggested next-chat handoff prompt

Use this document plus the canonical Full1902 documents as the starting point.

Suggested prompt:

```text
Read docs/FULL1902_LEGACY_CLEANUP_REVIEW_HANDOFF_2026-09-04.md and the canonical
Full 1902 Implementation documents. Re-check latest main before making assumptions.
Cleanup A/B are complete. The handoff's 2026-09-04 policy update is authoritative for
follow-up cleanup planning: Developer source may remain but must be disconnected from
production legacy architecture; Controller Software / ClawTweaks / HHC manager detection
and classification are cleanup targets. Start with Cleanup C unless latest code shows the
scope has already changed.
```
