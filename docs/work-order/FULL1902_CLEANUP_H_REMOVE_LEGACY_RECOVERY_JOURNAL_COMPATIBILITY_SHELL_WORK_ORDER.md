# Work Order — Full1902 Cleanup H: Remove Legacy RecoveryJournal Compatibility Shell

## Status

Focused cleanup work order for removing the last **routing-era controller `recovery.json` compatibility shell** after Cleanup G closed the write side.

Completed sequence:

```text
PR #476 — Cleanup A: removed legacy Steam-session controller-routing authority
PR #477 — Cleanup B: removed legacy Center M dummy/MainUI suppression subsystem
PR #478 — Cleanup C: removed dead routing-specific power resume branches
PR #479 — Cleanup D: removed controller-software / third-party manager authority
PR #480 — Cleanup E: removed startup controller-environment authority shell
PR #481 — Cleanup F: removed dead virtual-recovery / identity-exclusion seams
PR #482 — Cleanup G: removed dead M1/M2 diagnostic + RecoveryJournal write/mutation side
```

Code-review baseline used for this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     db0eee6a98bc89f3d03525a8bca9a1ff75f5ee54
latest merged production PR: #482 — Full1902 Cleanup G
```

Read these before implementation and use the authority order in the Full1902 README:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/FULL1902_LEGACY_CLEANUP_REVIEW_HANDOFF_2026-09-04.md`
- `docs/work-order/FULL1902_CLEANUP_G_REMOVE_DEAD_DIAGNOSTIC_RECOVERY_WRITE_SIDE_WORK_ORDER.md`

---

# 1. Product decision for Cleanup H

Choose the handoff's **Option A**:

> Drop compatibility with old pre-release development-build controller `recovery.json` files and remove the routing-era RecoveryJournal architecture completely.

This project is still pre-release, and Cleanup G already established the decisive technical boundary:

```text
current production Runtime
→ cannot create recovery.json
→ cannot update recovery.json
→ cannot acquire a RecoveryJournal mutation/lease
```

Therefore every remaining controller RecoveryJournal consumer exists only to interpret or block on **historical files written by older development builds**.

Do not preserve those consumers solely for development-state compatibility.

### Important distinction

This work order removes only the old controller mutation journal:

```text
SteamInputAddonforClaw-Data\recovery.json
```

It does **not** remove unrelated current ownership/recovery files such as, for example:

```text
display-resolution-recovery.json
intel-fps-limit-ownership.json
steam-cef-marker.json
provisioning receipts
```

Those have separate current owners and policies.

### No migration shim

Do not add a one-shot parser/migrator/retirement manager for old controller `recovery.json`.

After Cleanup H, an old file left on disk by a pre-release build is simply **inert legacy data**. Current production must not read it, gate on it, replay it, clean state from it, or recreate a replacement abstraction around it.

The existing full-reset/uninstall data-root cleanup may eventually remove such an inert file as part of deleting the whole app data root. Do not add a dedicated controller-journal deletion path just for historical development files.

---

# 2. Current product invariants that must remain unchanged

There are still exactly two controller-authority modes:

```text
Center M startup roots exactly Enabled
→ MSI / stock controller authority
→ desired physical PID1901
→ Addon controller stack passive

Center M startup roots exactly Disabled
→ Addon Runtime controller authority
→ desired physical PID1902
→ Addon Runtime mandatory
→ persistent Addon HidHide authority
→ one canonical VIIPER runtime
→ exactly one live Xbox360 or SteamDeck presentation
```

Steam/BPM remains presentation-only:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

Current Full1902 recovery remains **current-world reconciliation**:

```text
actual current authority
+ actual physical PID / strong identity
+ current DirectInput health
+ current deterministic HidHide baseline
+ current VIIPER presentation state
→ reconcile toward current desired state
→ read-back / verify
→ fail closed on real operation failure
```

Cleanup H must not recreate a historical mutation journal under a new name.

---

# 3. Current-code proof — the remaining journal is compatibility-only

## 3.1 Recovery namespace is now only the legacy controller journal

At baseline `db0eee6a...`, `src/SteamInputAddonforClaw/Recovery/` contains exactly:

```text
RecoveryJournal.cs
RecoveryJournalStore.cs
RecoveryManager.cs
```

Cleanup G reduced `RecoveryManager` to:

```text
HasIncompleteRecovery
LoadJournal
IsValidJournal
```

There is no current write/mutation API.

Therefore deleting these files does not remove a current mutation owner.

## 3.2 Startup Enabled path uses the journal only for old-build cleanup

Current `StartupCoordinator` performs:

```text
Center M Enabled
→ stable topology
→ establish verified stock PID1901 baseline
→ ResolveStaleRecoveryAsync()
   → LoadJournal()
   → optionally StartupHidHideRecoveryCleaner.TryClean(old journal)
   → delete old journal
→ RecoverySafe=true
```

No current Runtime can create that journal.

After Option A, the correct startup contract becomes:

```text
Center M Enabled
→ stable topology
→ establish verified stock PID1901 baseline
→ RecoverySafe=true
```

Do not make current startup safety depend on a file current production cannot create.

## 3.3 Disabled boot journal gate is historical only

Current `DisabledBootControllerAdmission` does:

```text
1. verify runtime prerequisites
2. require RecoveryManager.LoadJournal() == NoRecoveryNeeded
3. normalize + read-back verify deterministic Disabled HidHide baseline
```

Step 2 exists only to reject old routing-era state.

Current policy already says every Disabled boot must normalize current HidHide state to the deterministic Addon baseline. That current-world normalization is the real safety primitive.

After Cleanup H the admission contract should be:

```text
prerequisites ready
+ deterministic HidHide baseline normalized and verified now
= admission ready
```

No historical journal authority remains.

## 3.4 Runtime/power journal checks are also historical only

`AddonRuntimeComposition` passes:

```text
() => recoveryManager.HasIncompleteRecovery
```

to `AddonRuntimeHost`, which forwards it to `PowerTransitionCoordinator` and `UserTerminationGuard`.

Current resume still contains:

```text
if (_hasIncompleteRecovery())
    → RecoverySafety.Unsafe
    → keep gate closed
    → return
```

But Cleanup G guarantees current production cannot create that condition.

The branch therefore protects only historical pre-release files and must be removed with the journal shell.

## 3.5 MachineRecoverySafetyInspector is journal-only legacy policy

`MachineRecoverySafetyInspector` exists to enumerate Windows profiles and search for old controller recovery journals.

This conflicts with the current supported product scope:

```text
1 Windows user
1 interactive session
Fast User Switching / RDP / multi-session unsupported
```

and it no longer protects a state current production can create.

Additionally, the current canonical app data root is:

```text
SteamInputAddonforClaw-Data
```

while `MachineRecoverySafetyInspector` still hard-codes a historical relative path under:

```text
AppData\Local\SteamInputAddonforClaw\recovery.json
```

Do not repair or generalize this scanner. Delete it.

## 3.6 RecoveryMutationOwned termination reason has no live producer

`UserTerminationGuard` currently blocks when:

```text
recoverySafetyState.Current == Safe
&& hasIncompleteRecovery()
```

and reports:

```text
RecoveryMutationOwned
```

There is no current mutation writer after Cleanup G.

Remove this branch/reason rather than preserving a dead synthetic state.

---

# 4. Scope boundary

## In scope

1. remove the complete controller `RecoveryJournal` source directory;
2. remove `StartupHidHideRecoveryCleaner` and its tests;
3. remove stale-journal load/cleanup/retirement from `StartupCoordinator`;
4. remove the journal dependency from Disabled-boot admission;
5. remove `RuntimeRecoveryManager` from startup/runtime composition;
6. remove `hasIncompleteRecovery` from runtime host and power coordinator;
7. remove the journal-derived termination block and `RecoveryMutationOwned` reason;
8. delete `MachineRecoverySafetyInspector` and its dedicated test graph;
9. remove only the recovery-journal part of elevated prerequisite safety gating;
10. remove `AddonDataPaths.RecoveryJournalPath` / `ResolveRecoveryJournalPath` and corresponding tests;
11. remove obsolete `using SteamInputAddonforClaw.Recovery` imports;
12. remove all tests whose only purpose is old controller journal compatibility;
13. prove zero production/test references to the deleted journal types/symbols.

## Explicitly out of scope

Do not combine Cleanup H with:

```text
RecoverySafetyState deletion or rename
PowerMutationGate redesign
power barrier / epoch changes
PowerTransitionWatcher changes
suspend quiesce deadline changes
stock PID1901 resume-baseline behavior
PowerResumeObserved removal
Full1902 owned-controller resume/PnP recovery changes
MsiClawAddonPhysicalOwnership changes
MsiClawAddonPresentation changes
HidHide deterministic baseline algorithm changes
PID1901 ↔ PID1902 switching changes
Center M startup-root authority changes
Enable/Disable Center M transition changes
mandatory Runtime startup policy changes
VIIPER ownership / teardown changes
frontend protocol changes
unrelated app-data recovery/ownership files
RoutingTransition termination cleanup
broad RecoverySafety naming cleanup
```

The purpose is to remove the obsolete **historical journal compatibility authority** while preserving actual current lifecycle safety.

---

# 5. Required change A — delete the controller Recovery namespace

Delete:

```text
src/SteamInputAddonforClaw/Recovery/RecoveryJournal.cs
src/SteamInputAddonforClaw/Recovery/RecoveryJournalStore.cs
src/SteamInputAddonforClaw/Recovery/RecoveryManager.cs
```

This removes:

```text
RecoveryJournal
RecoveryMutationState
AddonOwnedVirtualDeviceRecoveryEntry
RecoveryStatus
RecoveryResult
IRecoveryJournalStore
RecoveryJournalStore
RecoveryManager
CurrentSchemaVersion
HasIncompleteRecovery
LoadJournal
IsValidJournal
```

Do not replace them with:

```text
LegacyRecoveryReader
RecoveryCompatibilityService
OldJournalInspector
RecoveryPresenceProbe
RecoveryMigrationManager
ControllerRecoveryStateStore
```

The entire controller journal concept is retired.

Historical work-order/docs references may remain as historical documentation; zero-reference requirements below apply to current production/test source, not archived design prose.

---

# 6. Required change B — simplify StartupCoordinator

File:

```text
src/SteamInputAddonforClaw/Startup/StartupCoordinator.cs
```

## 6.1 Remove journal fields and constructor parameters

Delete:

```text
IRecoveryJournalStore _recoveryJournalStore
RecoveryManager _recoveryManager
IStartupHidHideRecoveryCleaner? _hidHideRecoveryCleaner
```

Remove constructor parameters:

```text
recoveryJournalStore
hidHideRecoveryCleaner
```

Do not replace them with generic startup-recovery callbacks.

## 6.2 Delete the stale journal path

Delete:

```text
ResolveStaleRecoveryAsync(...)
TryRetireStaleStartupJournal(...)
```

and all journal-specific logs/comments such as:

```text
Stale startup journal ...
Recovery journal ...
JournalDeleted
DiscardOnly
```

## 6.3 Enabled authority startup result

Preserve this order:

```text
supported hardware
→ Center M startup roots == Enabled
→ stable physical topology
→ StockCenterMStartupBaseline.EstablishAsync
→ success only if verified baseline succeeds
```

After a successful stock baseline, return:

```text
RecoverySafe = true
```

directly.

If the baseline is unavailable or fails, keep:

```text
RecoverySafe = false
```

Do not weaken hardware/topology/baseline fail-close behavior.

## 6.4 Disabled authority startup

Do not add journal retirement or old-file cleanup to the Disabled branch.

It remains:

```text
supported hardware
→ Center M startup roots == Disabled
→ stable topology
→ DisabledBootControllerAdmission
```

The admission itself is simplified in Change C.

---

# 7. Required change C — remove RecoveryJournal from DisabledBootControllerAdmission

File:

```text
src/SteamInputAddonforClaw/Startup/DisabledBootControllerAdmission.cs
```

Current constructor:

```csharp
new DisabledBootControllerAdmission(
    prerequisiteInspector,
    loadRecoveryJournal,
    normalizeHidHideBaseline)
```

Target constructor:

```csharp
new DisabledBootControllerAdmission(
    prerequisiteInspector,
    normalizeHidHideBaseline)
```

Delete:

```text
Func<RecoveryResult> loadRecoveryJournal
RecoveryResult / RecoveryStatus logic
RecoveryJournalUnavailable reason
RecoveryJournal=<status> reason
"RecoveryJournal=Clear" logging
```

### Target evaluation order

```text
1. Runtime prerequisite inspection
2. deterministic Disabled-mode HidHide normalization + read-back verification
3. Ready
```

Preserve fail-close behavior:

```text
prerequisite inspect throws     → Blocked
prerequisites not Ready         → Blocked
HidHide normalization throws    → Blocked
HidHide result not compliant    → Blocked
```

Do not make HidHide best-effort.

The product invariant remains:

> No physical PID1902 ownership or non-neutral virtual presentation proceeds until the deterministic Disabled HidHide baseline is proven on the current boot.

---

# 8. Required change D — delete StartupHidHideRecoveryCleaner

Delete:

```text
src/SteamInputAddonforClaw/Startup/StartupHidHideRecoveryCleaner.cs
```

and its dedicated tests, expected from fresh closure to include:

```text
tests/SteamInputAddonforClaw.Tests/StartupHidHideRecoveryCleanerTests.cs
```

Delete:

```text
IStartupHidHideRecoveryCleaner
StartupHidHideRecoveryCleaner
RequiresCleanup(...)
old journal ownership-evidence validation
old whitelist-path evidence validation
old HiddenDeviceAdditions replay cleanup
OriginalHidHideActiveState historical restoration
```

Do not port this logic into the current deterministic `AddonControllerHidHideBaseline`.

Current Disabled-mode HidHide ownership already uses current-world normalization and read-back verification.

Current Enabled-mode authority release/transition logic has its own current policy. Do not invent historical third-party restoration from the deleted journal.

---

# 9. Required change E — simplify AddonStartupComposition

File:

```text
src/SteamInputAddonforClaw/Startup/AddonStartupComposition.cs
```

Delete construction of:

```text
RecoveryJournalStore
RecoveryManager runtimeRecoveryManager
StartupHidHideRecoveryCleaner
```

Remove `RuntimeRecoveryManager` from `AddonStartupComposition`.

Target composition should directly build:

```text
device enumerator / adapter / classifier / registry
stock Center M baseline
CenterMStartupControl
runtime prerequisite inspector
AddonControllerHidHideBaseline
disabled-boot admission
StartupCoordinator
```

without any controller-journal object.

Update the Disabled admission construction to the two required dependencies only:

```text
prerequisiteInspector
normalizeHidHideBaseline
```

Update `StartupCoordinator` construction to omit journal/store/cleaner arguments.

Do not introduce a generic recovery facade to preserve constructor shape.

---

# 10. Required change F — remove RecoveryManager from runtime composition

Files:

```text
src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs
```

## 10.1 AddonRuntimeCompositionFactory

Remove the `RecoveryManager recoveryManager` parameter.

Delete:

```csharp
() => recoveryManager.HasIncompleteRecovery
```

from `AddonRuntimeHost` construction.

Preserve:

```text
RecoverySafetyState initialization from StartupResult.RecoverySafe
PowerMutationGate
stockCenterMAuthority
stock PID1901 resume baseline callback
Steam/BPM observation
status RecoverySafe projection
PowerResumeObserved
```

## 10.2 AddonProcessHost

Update the call site so it no longer reads:

```text
startupComposition.RuntimeRecoveryManager
```

Do not change Full1902 physical/presentation ownership composition.

---

# 11. Required change G — remove journal gating from PowerTransitionCoordinator

File:

```text
src/SteamInputAddonforClaw/Power/PowerTransitionCoordinator.cs
```

Delete:

```text
Func<bool> _hasIncompleteRecovery
hasIncompleteRecovery constructor parameter
default (() => true)
resume branch that fails closed when a journal exists
journal-specific warning text
```

### Preserve the real resume sequence

After Cleanup H, resume remains conceptually:

```text
resume observation / epoch validation
→ keep forward gate closed
→ RecoverySafety = Indeterminate
→ emit PowerResumeObserved
→ if recoveryEnabled == false: remain Unsafe
→ establish stock baseline when stock authority requires it
→ verify epoch still authoritative
→ commit RecoverySafety Safe/Unsafe and gate state
→ afterRecovery current-world refresh/reconcile
```

Do not modify:

```text
PowerMutationGate
barrier application
Epoch checks
_suspendQuiesceBudget
participant quiesce
RecoverySafetyState
stock baseline callback
_afterRecovery
PowerResumeObserved
Steam refresh after resume
```

### Center M Disabled behavior

Current runtime composition already gives the generic stock baseline callback a successful no-op when stock authority is not selected.

The actual Addon-owned controller resume/recovery path remains driven by:

```text
PowerResumeObserved
→ AddonProcessHost current Full1902 owned-controller recovery/reconcile
```

Do not move that ownership into `PowerTransitionCoordinator`.

---

# 12. Required change H — simplify AddonRuntimeHost and UserTerminationGuard

Files:

```text
src/SteamInputAddonforClaw/Runtime/AddonRuntimeHost.cs
src/SteamInputAddonforClaw/Lifecycle/UserTerminationGuard.cs
```

## 12.1 AddonRuntimeHost

Remove constructor parameter:

```text
Func<bool> hasIncompleteRecovery
```

Construct `PowerTransitionCoordinator` without it.

Current `UserTerminationGuard` wiring:

```csharp
new UserTerminationGuard(
    () => _shutdownCancellation.IsCancellationRequested,
    () => recoverySafetyState.Current == RecoverySafety.Safe && hasIncompleteRecovery())
```

must lose the journal-derived second callback.

## 12.2 UserTerminationGuard

Remove:

```text
_liveRecoveryMutationOwned
RecoveryMutationOwned
```

Target lower-level guard should protect the current real process fact:

```text
RuntimeShuttingDown
```

and otherwise allow ordinary termination at this layer.

The separate mandatory-authority composition remains authoritative:

```text
Center M Disabled
→ UserTerminationComposition
→ ControllerAuthorityMandatory
→ intentional Runtime exit blocked
```

Preserve:

```text
RuntimeShuttingDown
ControllerAuthorityMandatory
MandatoryControllerRuntimePolicy
UserTerminationComposition
```

Do not fold mandatory-authority policy into the lower-level guard merely because one branch was removed.

`RoutingTransition` is out of scope unless fresh zero-reference closure shows it is a trivial enum-only residue and deleting it is required for compilation. Do not broaden this PR into another termination-policy redesign.

---

# 13. Required change I — delete MachineRecoverySafetyInspector

Delete:

```text
src/SteamInputAddonforClaw/Prerequisites/MachineRecoverySafetyInspector.cs
```

and dedicated tests:

```text
tests/SteamInputAddonforClaw.Tests/MachineRecoverySafetyInspectorTests.cs
```

This removes the journal-only graph:

```text
RecoverySafetyStatus
RecoverySafetyAssessment
IMachineRecoverySafetyInspector
IWindowsProfilePathSource
IRecoveryJournalPresenceProbe
IProfileDirectoryProbe
WindowsProfileListPathSource
FileRecoveryJournalPresenceProbe
FileSystemProfileDirectoryProbe
MachineRecoverySafetyInspector
```

Do not replace it with another multi-profile scanner.

The supported product is one Windows user / one interactive session; FUS/RDP/multi-session are out of scope.

---

# 14. Required change J — preserve elevated prerequisite safety minus the journal scan

File:

```text
src/SteamInputAddonforClaw/Prerequisites/ElevatedPrerequisiteSetup.cs
```

Current `EvaluateSafetyGate()` includes:

```text
hardware compatibility
→ MachineRecoverySafetyInspector
→ Steam RunningAppID / Big Picture safety gate
```

Remove only the obsolete journal component:

```csharp
var recoverySafety = new MachineRecoverySafetyInspector().Inspect();
if (!AllowsRecoverySafeProvisioning(recoverySafety)) return (false, recoverySafety.Reason);
```

Delete the now-dead helper:

```text
AllowsRecoverySafeProvisioning(...)
```

### Preserve the actual operation safety gates

`EvaluateSafetyGate()` must continue to require:

```text
supported hardware / AllowsMutation
Steam RunningAppID safe state
Big Picture safe state
```

and all existing trusted provisioning storage, installer hash, receipt, package/runtime verification, setup mutex, and bounded post-install verification behavior.

Do not turn prerequisite installation into an unconditional mutation path.

---

# 15. Required change K — remove RecoveryJournalPath from AddonDataPaths

File:

```text
src/SteamInputAddonforClaw/Install/AddonDataPaths.cs
```

Delete:

```text
RecoveryJournalPath
ResolveRecoveryJournalPath(...)
```

Update `AddonDataPathsTests` so controller `recovery.json` is no longer part of the current data-path contract.

Do not alter:

```text
SettingsPath
ProfilesPath
LogDirectory
CefMarkerOwnershipPath
DisplayResolutionRecoveryPath
IntelFpsLimitOwnershipPath
DeleteFullResetRoot
```

An old controller `recovery.json` may remain as inert pre-release data in an existing data root. Do not add a migration helper solely to find/delete it.

---

# 16. RecoverySafetyState is NOT a Cleanup H deletion target

This distinction is mandatory.

Keep:

```text
src/SteamInputAddonforClaw/Power/RecoverySafetyState.cs
RecoverySafety.Safe
RecoverySafety.Unsafe
RecoverySafety.Indeterminate
RecoverySafetyState
```

Why:

`RecoverySafetyState` now represents the **live current-process power/recovery boundary**, not historical journal ownership.

It is used for real supported lifecycle behavior:

```text
startup baseline failed
→ Unsafe

suspend begins
→ Indeterminate

resume baseline/reconcile succeeds
→ Safe

resume operation fails
→ Unsafe
```

and feeds the current operational status / forward mutation gate.

Deleting it merely because its name contains "Recovery" would weaken actual sleep/resume safety.

Do not rename it in this PR. Naming polish can be considered separately only if it materially improves clarity without changing ownership.

---

# 17. StartupResult.RecoverySafe is retained

Keep:

```text
StartupResult.RecoverySafe
```

for this cleanup.

After journal removal its meaning becomes cleaner:

```text
Center M Enabled
+ supported hardware
+ stable topology
+ stock PID1901 baseline successfully established
→ RecoverySafe=true
```

Other startup paths remain false unless already defined otherwise.

This bool still initializes the live `RecoverySafetyState` / generic power-recovery permission boundary.

Do not combine Cleanup H with a redesign of that startup/power contract.

---

# 18. Tests — required updates

Delete journal-only suites, expected from fresh closure to include:

```text
RecoveryManagerTests
RecoveryJournalStoreTests
StartupHidHideRecoveryCleanerTests
MachineRecoverySafetyInspectorTests
```

Update all fakes/constructors that implemented `IRecoveryJournalStore` or supplied `hasIncompleteRecovery`.

## 18.1 StartupCoordinator tests

Required retained/new assertions:

```text
Update restart scheduled
→ hardware/topology/baseline not run

Unsupported/indeterminate hardware
→ passive

Center M Enabled + unstable topology
→ passive / RecoverySafe false

Center M Enabled + stable topology + stock baseline failure
→ RecoverySafe false

Center M Enabled + stable topology + stock baseline success
→ RecoverySafe true
→ no journal dependency exists

Center M Disabled + stable topology
→ DisabledBootControllerAdmission evaluated
→ no stock baseline
→ no journal load/cleanup/delete path

Partial/Unavailable Center M roots
→ no owner selected / passive
```

Remove tests that manufacture old schema-v5 files or assert stale journal cleanup/retirement.

## 18.2 DisabledBootControllerAdmission tests

Target matrix:

```text
Prerequisite inspector throws              → Blocked
Prerequisites not routing-ready             → Blocked
Prerequisites ready + HidHide throws        → Blocked
Prerequisites ready + HidHide noncompliant  → Blocked
Prerequisites ready + HidHide compliant     → Ready
```

Prove HidHide normalization does not run if prerequisite inspection already blocks.

No RecoveryJournal fixture should remain.

## 18.3 PowerTransitionTests

Remove journal-specific cases such as:

```text
journal remains on resume → Unsafe
hasIncompleteRecovery callback behavior
```

Preserve/verify real power behavior:

```text
suspend barrier closes mutation gate
participant quiesce deadline/failure remains fail-closed
stale resume epoch cannot overwrite newer suspend authority
recoveryEnabled=false remains Unsafe
stock-authority resume baseline success opens gate
stock-authority resume baseline failure remains Unsafe
post-recovery failure closes gate / Unsafe
PowerResumeObserved still fires on authoritative resume
```

Do not weaken the existing epoch/barrier tests.

## 18.4 AddonRuntimeHost tests

Remove `hasIncompleteRecovery` fixture plumbing and `RecoveryMutationOwned` tests.

Preserve:

```text
power observation registration fail-close
PowerResumeObserved delivery
Steam refresh on resume
shutdown-safe Steam lifecycle lock behavior
RuntimeShuttingDown termination block
normal dispose idempotence
```

## 18.5 UserTerminationGuard tests

Delete:

```text
RecoveryMutationOwned
```

coverage.

Keep/prove:

```text
RuntimeShuttingDown blocks
ordinary lower-level state allows termination
ControllerAuthorityMandatory still blocks after composition when Center M Disabled
```

## 18.6 Elevated prerequisite tests

Update safety-gate tests so they prove:

```text
unsupported/unsafe hardware still blocks
active Steam RunningAppID still blocks as currently defined
Big Picture unsafe state still blocks as currently defined
journal/profile scanning no longer participates
```

Do not remove unrelated installer/provisioning safety tests.

---

# 19. Source/reference closure requirements

After implementation, production/test source should have zero references to:

```text
RecoveryJournal
RecoveryMutationState
AddonOwnedVirtualDeviceRecoveryEntry
RecoveryManager
IRecoveryJournalStore
RecoveryJournalStore
RecoveryStatus
RecoveryResult
CurrentSchemaVersion
HasIncompleteRecovery
LoadJournal
StartupHidHideRecoveryCleaner
IStartupHidHideRecoveryCleaner
MachineRecoverySafetyInspector
IMachineRecoverySafetyInspector
IRecoveryJournalPresenceProbe
RecoveryJournalPath
ResolveRecoveryJournalPath
RecoveryMutationOwned
hasIncompleteRecovery
```

Historical docs/work-orders may still contain these strings.

Also verify:

```text
PowerResumeObserved still exists and is wired to AddonProcessHost
RecoverySafetyState still exists
PowerMutationGate still exists
StockCenterMStartupBaseline resume path still exists for Enabled authority
AddonControllerHidHideBaseline still performs deterministic normalization/readback
Disabled boot still blocks on real prerequisite/HidHide failures
```

---

# 20. Failure policy after Cleanup H

The app must fail closed on **actual current failures**, not on deleted historical state.

Still fail closed for:

```text
unsupported/ambiguous hardware
unstable PnP topology
Center M roots Partial/Unavailable
runtime prerequisite inspection failure
HidHide unavailable/unreadable
required HidHide mutation failure
HidHide readback mismatch
physical identity ambiguity/change at mutation boundary
PID switch / PnP settle failure
DirectInput acquire/read failure
VIIPER attach/detach/teardown failure
suspend quiesce failure
resume baseline failure
stale power epoch
mandatory Runtime startup failure while entering Disabled authority
```

Do **not** fail closed merely because an old pre-release `recovery.json` happens to exist on disk.

---

# 21. Overengineering guardrails

Do not add:

```text
legacy-file migration state
schema retirement registry
recovery epoch for old files
startup compatibility manager
cross-user recovery scanner
profile authority abstraction
journal tombstone
one-time migration setting
RecoverySafety wrapper
new persistent controller authority bit
```

No new lock/epoch/barrier is required for this cleanup.

The current supported lifecycle already has the needed authorities:

```text
Center M startup roots      → controller authority intent
MsiClawAddonPhysicalOwnership → physical owner/reconcile
AddonControllerHidHideBaseline → current isolation baseline
CanonicalViiperRuntime / presentation owner → virtual output
PowerMutationGate + RecoverySafetyState → live suspend/resume mutation safety
```

Delete historical compatibility state rather than replacing it.

---

# 22. Expected production files touched

Fresh closure may refine the exact list, but expect at minimum:

```text
DELETE src/SteamInputAddonforClaw/Recovery/RecoveryJournal.cs
DELETE src/SteamInputAddonforClaw/Recovery/RecoveryJournalStore.cs
DELETE src/SteamInputAddonforClaw/Recovery/RecoveryManager.cs
DELETE src/SteamInputAddonforClaw/Startup/StartupHidHideRecoveryCleaner.cs
DELETE src/SteamInputAddonforClaw/Prerequisites/MachineRecoverySafetyInspector.cs

EDIT src/SteamInputAddonforClaw/Startup/StartupCoordinator.cs
EDIT src/SteamInputAddonforClaw/Startup/DisabledBootControllerAdmission.cs
EDIT src/SteamInputAddonforClaw/Startup/AddonStartupComposition.cs
EDIT src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs
EDIT src/SteamInputAddonforClaw/Runtime/AddonRuntimeComposition.cs
EDIT src/SteamInputAddonforClaw/Runtime/AddonRuntimeHost.cs
EDIT src/SteamInputAddonforClaw/Power/PowerTransitionCoordinator.cs
EDIT src/SteamInputAddonforClaw/Lifecycle/UserTerminationGuard.cs
EDIT src/SteamInputAddonforClaw/Prerequisites/ElevatedPrerequisiteSetup.cs
EDIT src/SteamInputAddonforClaw/Install/AddonDataPaths.cs
```

Plus focused test deletions/updates.

Do not touch unrelated Full1902 owner implementations merely because tests share constructors.

---

# 23. Acceptance criteria

Cleanup H is complete when all are true:

1. Current production contains no controller `RecoveryJournal` type/store/manager.
2. Current production never reads, writes, validates, deletes, or gates on controller `recovery.json`.
3. `AddonDataPaths` no longer exposes a controller recovery-journal path.
4. Center M Enabled startup becomes safe after current hardware/topology + verified stock baseline only.
5. Center M Disabled admission depends on current prerequisites + deterministic HidHide normalization/readback only.
6. No stale journal cleanup/replay/retirement code remains.
7. `PowerTransitionCoordinator` has no incomplete-journal callback/branch.
8. `RecoverySafetyState`, power barrier/epoch, resume baseline, and `PowerResumeObserved` remain intact.
9. `RecoveryMutationOwned` is removed from user termination policy.
10. Mandatory Runtime termination block while Center M Disabled remains intact.
11. Elevated prerequisite setup still preserves hardware + Steam/BPM + package/provisioning safety without journal/profile scanning.
12. No new compatibility manager/migration/state machine is introduced.
13. Debug build succeeds with zero warnings/errors.
14. Release build succeeds with zero warnings/errors.
15. Full Release test suite passes.
16. `git diff --check` is clean.
17. No frontend protocol bump unless an unrelated compile-enforced contract change unexpectedly requires one; this cleanup itself should require none.

---

# 24. Suggested PR title

```text
Full1902 Cleanup H: remove legacy RecoveryJournal compatibility shell
```

Suggested summary:

```text
Cleanup G made controller recovery.json read-only legacy input by removing every current writer/mutation API. Cleanup H drops pre-release old-file compatibility entirely: delete the controller RecoveryJournal namespace, stale startup cleaner/retirement, Disabled-boot journal gate, runtime/power journal checks, RecoveryMutationOwned termination reason, and the multi-profile journal scanner. Preserve current Full1902 safety authorities: deterministic HidHide normalization, physical/PnP recovery, VIIPER teardown, mandatory Runtime authority, PowerMutationGate/RecoverySafetyState, stock PID1901 resume baseline, and PowerResumeObserved current-world reconciliation.
```
