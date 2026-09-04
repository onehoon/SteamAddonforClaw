# Full1902 Legacy Cleanup Review — Handoff

> Date: 2026-09-04  
> Repository: `onehoon/SteamAddonforClaw`  
> Review baseline: `main` at `aa77801c8dfcb621b16b854f928fc8aefd538e89`  
> Purpose: conversation handoff / follow-up cleanup planning  
> This is **not** a work order and does not override the canonical Full1902 documents.

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

Cleanup A and Cleanup B removed the large legacy Steam-routing owner graph and the Center M dummy/MainUI suppression subsystem, but the current source still contains several old routing-era contracts around the new Full1902 owners.

The remaining pattern is roughly:

```text
current Full1902 physical/presentation/lifecycle owners
        +
old routing-era startup/recovery/status/developer shell
```

The objective of the next pass is not LOC reduction for its own sake. The objective is:

> one clear owner / one authority model / one teardown path / one failure policy, while preserving real handheld lifecycle safety.

Do not add state, locks, epochs, barriers, wrappers, managers, or generalized abstractions to replace code being deleted unless a real supported lifecycle requires them.

---

# 3. Confirmed cleanup candidates

## 3.1 PowerTransitionCoordinator still contains dead legacy routing branches

`PowerTransitionCoordinator` still has the following routing-era fields/callbacks:

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
RecoverySafetyState while it still has a real caller
stock baseline resume
current Full1902 post-resume controller recovery
```

This is the best first cleanup because it is mechanically isolated and does not require a product decision.

---

## 3.2 Startup ControllerEnvironmentMode is now a legacy authority abstraction

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

That model predates Full1902 authority.

Current Full1902 startup authority is instead decided by:

```text
supported MSI Claw?
        ↓
Center M startup roots
        ↓
Enabled  → stock authority
Disabled → Addon authority
Partial / unavailable → fail closed / passive
```

Therefore `StockCenterM / ClawTweaks / HHCManaged` should no longer be controller-authority modes.

### Related legacy field

`StartupResult.LegacyRoutingAllowed` is also a legacy contract. The useful fact it now approximates is already represented more accurately downstream as:

```text
stockCenterMAuthority
```

The legacy name should not survive merely to keep old tests/contracts compiling.

### Cleanup direction

Target removal/simplification of:

```text
ControllerEnvironmentMode
StartupControllerEnvironmentMapper
LegacyRoutingAllowed
old ClawTweaksEnvironmentDetector authority mapping
mode-dependent startup branches that cannot occur in current production
```

Keep the real controller-topology stabilization logic. Do not weaken:

```text
stable snapshot requirement
bounded timeout
MSI Claw internal topology recognition
PID1901/PID1902 control-HID readiness
PnP re-enumeration handling
```

`ControllerEnvironmentWaiter` may be simplified to topology readiness rather than old environment-mode readiness if reference closure supports it.

---

## 3.3 Environment Discovery currently holds part of the old startup model

Developer `Environment Discovery` still records:

```text
CurrentDetectionDiscoveryInfo
→ Software
→ ControllerEnvironment
→ EnvironmentReadiness
```

The report generator still calls the legacy startup environment mapper.

This does **not** require deleting Environment Discovery. The diagnostic is still useful.

### Recommended adjustment

Keep the developer report, but change it to report Full1902 facts directly, for example:

```text
HardwareSupport
CenterMStartupState = Enabled / Disabled / Partial / Unavailable
ControllerManager = None / ClawTweaks / Handheld Companion / ...
Prerequisite state
current physical PID/topology facts where already available
```

Do not recreate a generalized controller-environment authority abstraction just for the report.

---

## 3.4 M1M2DiagnosticCoordinator is dead production code

`Diagnostics/M1M2DiagnosticCoordinator.cs` remains in the tree, but current production/UI composition does not create it.

The current Developer menu contains:

```text
Test Mode
Environment Discovery
Vibration Test
Gyro / Sensor Test
Fan Hardware Probe
Logging
```

There is no M1/M2 diagnostic UI entry.

The coordinator still references old RecoveryManager mutation/lease APIs, so it helps keep the routing-era recovery model alive.

### Cleanup direction

Remove `M1M2DiagnosticCoordinator` and its dedicated tests if fresh reference closure confirms no production caller.

Future M1/M2 remapping should be designed on the current Full1902 presentation/input model, not retained through this diagnostic class.

---

## 3.5 StartupVirtualOutputRecoveryInspector is a legacy recovery leaf

`StartupVirtualOutputRecoveryInspector` and `IStartupVirtualOutputRecoveryInspector` remain, but current production startup composition does not wire them.

They operate on old `AddonOwnedVirtualDeviceRecoveryEntry` journal state.

### Cleanup direction

Delete the inspector/interface/tests if fresh grep confirms no current production call path.

Do not replace it with a new startup virtual-device inspector unless a current Full1902 lifecycle path actually needs one. Current presentation ownership already has its own VIIPER lifecycle/teardown logic.

---

## 3.6 AddonOwnedVirtualDeviceTracker is another legacy recovery/classifier seam

`VirtualOutput/Viiper/AddonOwnedVirtualDeviceTracker.cs` still implements `IControllerIdentityExclusionSource`, but current production composition does not create it.

The optional identity-exclusion seam in `ControllerDeviceClassifier` should be reviewed with it.

### Cleanup direction

If no current Full1902 owner feeds this tracker, delete it and simplify the classifier accordingly.

Do **not** remove current known-virtual recognition that is still required for controller-manager conflict/admission logic without first proving it is dead.

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
→ normalize to the deterministic current baseline
→ readback verify
→ fail closed on actual operation failure
```

The Addon explicitly does not maintain arbitrary third-party HidHide backup/restore ownership in current policy.

At the reviewed baseline, current physical/presentation owners do not appear to create new routing-era RecoveryJournal mutation sessions. However, many older consumers still read the journal and therefore keep the architecture alive.

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
legacy recovery tests
```

This requires a dedicated reference-closure pass before deletion.

---

## 4.1 Product decision: old recovery.json compatibility

One decision is needed before the major RecoveryJournal cleanup.

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
related frontend/setup status plumbing
legacy recovery tests
```

### Option B — keep only a one-shot retirement shim

If older installed development builds with `recovery.json` must be handled, do not preserve the full replay/restore architecture.

Prefer a bounded compatibility path:

```text
old recovery.json found
→ determine current Full1902 authority
→ normalize current HidHide/controller state using current policy
→ verify the current safe state
→ delete old recovery.json
```

No historical arbitrary third-party restoration, no mutation lease manager, no generalized replay state machine.

### Current recommendation

If development compatibility can be dropped, choose Option A.

---

# 5. MachineRecoverySafetyInspector is likely removable with RecoveryJournal

The current prerequisite-install safety gate still checks recovery state through a machine-wide/profile-oriented inspector.

The supported product scope is:

```text
1 Windows user
1 interactive session
Fast User Switching unsupported
RDP/multi-session unsupported
```

If RecoveryJournal is no longer a current production mutation authority, scanning other profiles for historical journal state becomes both unnecessary and outside the supported lifecycle model.

### Cleanup direction

Keep the HidHide/usbip-win2 prerequisite installer itself.

Remove only obsolete journal/profile recovery gating after the RecoveryJournal decision is made.

Do not weaken real prerequisite package verification or actual install-operation fail-close behavior.

---

# 6. UserTerminationGuard can simplify after RecoveryJournal cleanup

Current termination safety includes concepts such as:

```text
RuntimeShuttingDown
RecoveryMutationOwned
ControllerAuthorityMandatory
```

`ControllerAuthorityMandatory` is a real Full1902 invariant and must remain:

```text
Center M Disabled
→ Addon Runtime is mandatory
→ ordinary user Exit must not terminate controller authority
→ supported release path is Enable Center M + Restart / uninstall policy
```

`RecoveryMutationOwned` should be re-evaluated after RecoveryJournal deletion. Do not keep it solely to preserve a dead journal contract.

There is also a historical enum name `RoutingTransition` whose current meaning is already a controller-authority transition (for example deferred Disabled-mode acquisition/Win+G arming), not a Steam routing transition.

If still used after cleanup, rename it to a current authority term rather than adding a new state manager.

---

# 7. Developer Test Mode is now semantically obsolete

The Developer UI still exposes:

```text
Test Mode
Treat Steam session as active without launching a game.
```

This drives the old synthetic/effective Steam-session model.

Full1902 intentionally does **not** use this synthetic DeveloperTest session to select controller authority or the current X360/SteamDeck presentation.

Current Full1902 presentation is based on actual facts:

```text
actual RunningAppID
Big Picture state
```

Therefore Developer Test Mode no longer tests the real Full1902 controller presentation path.

### Recommended product decision

Remove Developer Test Mode rather than preserving a fake Steam-session authority.

Likely cleanup closure:

```text
DeveloperPage Test Mode card
DeveloperTestModeState
FrontendDeveloperSnapshot.TestModeEnabled
SetDeveloperTestMode RPC
FrontendSteamSource.DeveloperTest
synthetic EffectiveSteamSessionSource branch
related diagnostic/tests
```

Keep actual Steam/BPM observation used by current presentation and Device/Profile behavior.

---

# 8. Developer Vibration Test is currently a dead UI feature

The Developer menu still exposes:

```text
Vibration Test
Test Steam Deck rumble and haptic feedback through physical MSI Claw motors.
Requires Developer Test Mode.
```

But current frontend/runtime implementation explicitly returns unavailable because Cleanup A removed the legacy routing-owned vibration transport:

```text
LegacyRoutingRemoved
Developer vibration test is unavailable in this build
```

The old vibration session logging/RPC shell remains despite the operation being unavailable.

Also, `FeedbackAuthority` and `SteamDeckRumbleFeedbackBridge` currently have no confirmed production composition path at the reviewed baseline; they are primarily retained by tests/old vibration architecture.

### Recommended product decision

Remove the current Developer Vibration Test UI and old test transport now.

Likely cleanup closure:

```text
VibrationTestPage
OpenVibrationTestSession RPC
RunVibrationTest RPC
CloseVibrationTestSession RPC
VibrationTestSessionWriter
legacy vibration frontend mappings
FeedbackAuthority if no production caller
SteamDeckRumbleFeedbackBridge if no production caller
DeveloperVibrationTest tests
old session lifecycle tests
```

### Low-level MSI rumble primitives

Do not automatically delete useful RE-backed low-level protocol code just because the old developer shell is gone.

Candidate primitives to preserve if future Full1902 rumble implementation is near:

```text
MsiClawRumbleSink
MsiClawRumblePacketBuilder
TwoMotorRumble / physical write result primitives
```

The future production rumble path should be designed against the current `MsiClawAddonPresentation` / VIIPER ownership model, not the deleted routing-era feedback authority.

---

# 9. DiagnosticSessionTracker should be re-evaluated with Developer Test removal

`DiagnosticSessionTracker` still tracks:

```text
RawRunningAppID
EffectiveRunningAppID
EffectiveSource
DeveloperTest / BigPicture synthetic identity
```

This is another old effective-Steam-session concept.

If Developer Test / EffectiveSteamSessionSource is removed, simplify or delete only the Steam-session tracker portion if it no longer serves current diagnostics.

Do not remove the useful current controller-input diagnostics in the same file, such as:

```text
ControllerState change logging
DirectInput POV diagnostics
D-pad transition diagnostics
analog diagnostics
```

Those remain useful for real PID1902 input debugging.

---

# 10. ControllerEnvironmentCompatibility is still a stock-era status/setup contract

`SystemStatusProvider` now has a Full1902 owner-aware Addon status override, but it still computes the old controller-environment compatibility model underneath.

That compatibility model is centered around stock MSI Center M runtime/installation state, which creates a conceptual contradiction in healthy Disabled mode:

```text
actual Full1902 state:
Addon authority healthy
PID1902 live
DirectInput live
VIIPER presentation live

old compatibility:
MSI Center M not operational
→ Unsupported / Indeterminate-style result
```

The current Full1902 status override exists partly to prevent that old compatibility result from making normal Disabled mode appear unhealthy.

This suggests the old compatibility contract should eventually be removed rather than permanently carried underneath the new status model.

---

## 10.1 Status UI is not the main blocker

The current Status UI displays:

```text
Device
Steam Game
Controller Software
Routing Components
InfoBar
```

It does not display a dedicated ControllerEnvironment card.

However, `StatusPresentation.IsWarning()` still considers an indeterminate controller-environment status when deciding whether to show the warning InfoBar.

Therefore removing the old frontend compatibility contract requires updating the warning mapping and frontend DTOs/tests, but it does not require a replacement UI card.

Do not invent a new Full1902 Controller Status card as part of cleanup unless separately requested.

---

## 10.2 First-Time Setup is the stronger dependency

`FirstTimeSetupPolicy` still blocks setup when the old compatibility state is Unsupported or Indeterminate.

The frontend prerequisite executor feeds `snapshot.Compatibility` directly into the setup policy.

This means `ControllerEnvironmentCompatibility` cannot simply be deleted without updating setup gating.

### Full1902-oriented direction

Prerequisite installation should be gated by current facts such as:

```text
supported hardware
safe current authority/transition state
actual prerequisite package/install state
actual Steam/BPM safety condition where still required
actual operation/readback success
```

It should not universally require the Center M process to be running in a product that deliberately supports healthy Center M Disabled / Addon authority.

This is a cleanup + policy alignment task, not a reason to preserve the old compatibility abstraction forever.

---

# 11. Controller Software conflict detection must NOT be deleted

Do not confuse the old environment-authority model with current conflict detection.

The current startup composition intentionally creates status/detection providers for:

```text
MSI Center M
ClawTweaks
Handheld Companion
```

Those facts are used by current Disabled-boot/controller-manager admission and conflict safety.

Therefore the following are not automatically legacy:

```text
MsiCenterMSoftwareStatusProvider
ClawTweaksSoftwareStatusProvider
HandheldCompanionSoftwareStatusProvider
ControllerManagerClassification
current ClawTweaks/HHC installation/runtime probes
```

If the old `ClawTweaksEnvironmentDetector` file mixes obsolete authority mapping with useful current probes, split/move the useful primitives and delete only the obsolete environment mapping.

---

# 12. Small dead diagnostics worth cleaning

A fresh reference pass should include these small candidates:

```text
ClawTweaksCompatibilitySnapshotLogger
M1M2DiagnosticCoordinator
StartupVirtualOutputRecoveryInspector
AddonOwnedVirtualDeviceTracker
```

They appear to be old research/compatibility/recovery leaves rather than current Full1902 owners.

Delete only after fresh production-reference closure.

---

# 13. Do not delete current Full1902 lifecycle owners

The following are intentionally **not** cleanup targets merely because they are complex or originated before the final architecture:

```text
CenterMStartupControl
SteamInputAddonforClaw.CenterMStartupHelper
StockCenterMStartupBaseline
CenterMRebootAuthorityTransition

MsiClawDeviceAdapter
MsiClawNativeStateManager / current native mode control primitives
MsiClawAddonPhysicalOwnership
MsiClawInputSource / DirectInput primitives
AddonControllerHidHideBaseline
DisabledBootControllerAdmission current normalization/admission logic

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

ControllerManagerClassification
current ClawTweaks/HHC conflict probes

RoutingTraceContext where still used by current physical/VIIPER diagnostics
```

In particular, VIIPER `PendingCleanup` state is not automatically legacy. Current detach/teardown failure recovery is a real lifecycle concern and must remain if still used by the live presentation owner.

---

# 14. Recommended implementation order

A clean sequence would be:

## Cleanup C — dead Power routing branches

Scope:

```text
PowerTransitionCoordinator preserved-routing branch
PowerTransitionCoordinator residual-routing-cleanup branch
routing-only callbacks/fields/logs/tests
```

No product decision required.

---

## Cleanup D — startup authority-contract simplification

Scope:

```text
ControllerEnvironmentMode authority model
StartupControllerEnvironmentMapper
LegacyRoutingAllowed
old ClawTweaks/HHC startup environment mapping
mode-only tests
Environment Discovery migration to direct Full1902 facts
```

Preserve real topology stabilization.

---

## Cleanup E1 — dead recovery leaves

Scope:

```text
M1M2DiagnosticCoordinator
StartupVirtualOutputRecoveryInspector
AddonOwnedVirtualDeviceTracker if unreferenced
other dead journal-owned helpers
related tests
```

No broad RecoveryJournal rewrite yet.

---

## Cleanup E2 — RecoveryJournal architecture retirement

Prerequisite decision:

```text
drop old recovery.json compatibility
or
one-shot Full1902 normalization + retirement shim
```

Then remove the old mutation/replay/lease architecture and all dead consumers while preserving deterministic current ownership/teardown safety.

---

## Cleanup F — Developer legacy Steam/vibration removal

Recommended scope:

```text
Developer Test Mode UI/runtime/RPC
synthetic effective Steam session
FrontendSteamSource.DeveloperTest
current unavailable Vibration Test UI/RPC/session shell
legacy FeedbackAuthority/SteamDeckRumbleFeedbackBridge if no production caller
related tests
```

Keep useful low-level MSI rumble protocol primitives if desired for the future Full1902 rumble implementation.

---

## Cleanup G — status/setup compatibility alignment

Scope:

```text
ControllerEnvironmentCompatibility old stock-era contract
frontend compatibility DTO projection
Status warning dependency
FirstTimeSetup compatibility dependency
obsolete compatibility tests
```

Replace with direct current Full1902 facts only where needed. Do not create a new generalized status authority.

---

# 15. Decisions to carry into the next conversation

The next conversation should explicitly settle these before writing the later work orders:

1. **Recovery compatibility**
   - Can old development-build `recovery.json` simply be unsupported/removed?
   - Or is a one-shot retirement shim required?

2. **Developer Test Mode**
   - Recommendation: delete it because it no longer drives the actual Full1902 presentation.

3. **Developer Vibration Test**
   - Recommendation: remove the current unavailable UI/RPC path now and re-add a test only when Full1902 production rumble exists.

4. **Low-level rumble primitives**
   - Recommendation: keep the RE-backed MSI packet/sink primitives if production rumble work is near; delete only the old authority/test shell.

5. **Environment Discovery**
   - Recommendation: keep the feature, but migrate its output from old ControllerEnvironment mode to direct Full1902 authority/conflict/prerequisite facts.

6. **Controller Software status UI**
   - Recommendation: keep it. It exposes current conflict software and is not itself a routing authority.

---

# 16. Review / work-order policy for follow-up

For each cleanup work order:

- re-read the latest Full1902 authority documents before implementation;
- inspect latest `main` because this area is changing rapidly;
- fresh-grep every proposed deletion before writing the final file list;
- delete reference closures rather than preserving dead DTOs/interfaces solely for tests;
- keep real Sleep/Hibernate/Resume, Restart/Crash/Shutdown, PnP loss/re-enumeration, PID1901↔PID1902 restoration, HidHide ownership, VIIPER teardown, routing rollback/fail-close safety;
- do not defend theoretical instruction-level races by introducing new authority/state/manager layers;
- do not retain unsupported Fast User Switching/RDP/multi-session machinery;
- do not replace a deleted legacy abstraction with a new generalized abstraction unless an actual current production lifecycle requires it.

The target is not “fewer files” by itself. The target is:

```text
one current Full1902 controller authority model
one physical owner
one presentation owner
one deterministic HidHide policy
one clear teardown/recovery policy
no legacy Steam-routing authority shell around them
```

---

# 17. Suggested next-chat handoff prompt

Use this document plus the canonical Full1902 documents as the starting point.

Suggested prompt:

```text
Read docs/FULL1902_LEGACY_CLEANUP_REVIEW_HANDOFF_2026-09-04.md and the canonical
Full 1902 Implementation documents. Re-check latest main before making assumptions.
We already completed the major Cleanup A/B removals. Continue from the handoff and
prepare the next cleanup plan/work order, starting with Cleanup C unless latest code
shows that scope has already changed.
```
