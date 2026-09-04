# Work Order — Full1902 Cleanup F: Remove Dead Virtual-Recovery / Identity-Exclusion Seams

## Status

Focused deletion/simplification work order for removing the remaining **unwired virtual-device recovery / identity-exclusion seams** after:

```text
PR #476 — Cleanup A: removed legacy Steam-session controller-routing authority
PR #477 — Cleanup B: removed legacy Center M dummy/MainUI suppression subsystem
PR #478 — Cleanup C: removed dead routing-specific power resume branches
PR #479 — Cleanup D: removed controller-software / third-party manager authority
PR #480 — Cleanup E: removed the startup controller-environment authority shell
```

This is deliberately **not** the full `RecoveryJournal` cleanup yet.

It removes dead producers/inspectors and one dead classifier seam so that the later Recovery cleanup has a smaller, clearer reference closure without forcing the pending old-`recovery.json` compatibility decision into this PR.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     be230ea1c79169efdd60833b592721cd46bf5879
latest merged production PR: #480 — Full1902 Cleanup E
```

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/FULL1902_LEGACY_CLEANUP_REVIEW_HANDOFF_2026-09-04.md`
- `docs/work-order/FULL1902_CLEANUP_E_REMOVE_STARTUP_ENVIRONMENT_AUTHORITY_SHELL_WORK_ORDER.md`

The application is pre-release. Do not preserve dead production seams only for source compatibility or old unit tests.

---

# 1. Product invariants this cleanup must preserve

Current controller authority remains exactly:

```text
Center M startup roots exactly Enabled
→ MSI / stock controller authority
→ desired physical PID1901
→ no Addon DirectInput / HidHide physical isolation / VIIPER presentation ownership

Center M startup roots exactly Disabled
→ Addon Runtime controller authority
→ desired physical PID1902
→ persistent DirectInput ownership
→ deterministic Addon HidHide baseline
→ one Addon-owned VIIPER runtime
→ exactly one live X360 or SteamDeck presentation
```

Steam/BPM remains presentation-only:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

Current Full1902 presentation ownership is the live runtime path around:

```text
MsiClawAddonPresentation
→ CanonicalViiperRuntime
```

This cleanup must not introduce or preserve a second virtual-device owner merely because older recovery code once tracked created PnP instance IDs.

Current HidHide policy also remains deterministic current-world normalization:

```text
Addon authority selected
→ normalize current HidHide state to the exact Addon baseline
→ read back and verify
→ fail closed on actual operation failure
```

Do not restore the old model of maintaining arbitrary historical third-party HidHide/virtual-device ownership.

---

# 2. Goal

Remove this dead graph:

```text
AddonOwnedVirtualDeviceTracker
        ↓
IControllerIdentityExclusionSource
        ↓
ControllerDeviceClassifier.AddonOwnedVirtual branch

old RecoveryManager virtual-device mutation writers
        ↓
AddonOwnedVirtualDeviceRecoveryEntry producer path
        ↓
StartupVirtualOutputRecoveryInspector
```

while preserving the still-current read-only legacy `RecoveryJournal` schema for this PR.

Target end state:

```text
no live Addon-owned virtual-device tracker
no identity-exclusion source/fallback object
no AddonOwnedVirtual classification branch
no production API that records/resolves/completes virtual-device journal mutations
no startup virtual-output recovery inspector/interface/assessment

BUT
schema-v5 AddonOwnedVirtualDeviceRecoveryEntry may still deserialize as old recovery.json evidence
until the dedicated RecoveryJournal cleanup decides whether old development-build files are dropped or retired
```

No replacement tracker, registry, virtual-owner manager, recovery inspector, or classifier abstraction should be added.

---

# 3. Current-code proof — why this cleanup is safe now

## 3.1 `StartupVirtualOutputRecoveryInspector` has no production call path

Current source still contains:

```text
src/SteamInputAddonforClaw/Startup/StartupVirtualOutputRecoveryInspector.cs
src/SteamInputAddonforClaw/Startup/IStartupVirtualOutputRecoveryInspector.cs
```

The interface operates only on:

```csharp
IReadOnlyList<AddonOwnedVirtualDeviceRecoveryEntry>
```

and returns:

```text
StartupVirtualOutputRecoveryAssessment
```

Fresh reference review on current `main` finds these symbols only in:

```text
the interface/implementation themselves
legacy cleanup documentation
stale test doubles in StartupCoordinatorTests
```

`StartupCoordinator` no longer owns or invokes this inspector.

Its current stale-journal path is:

```text
LoadJournal
→ if HidHide evidence exists, StartupHidHideRecoveryCleaner
→ never replay native/virtual state
→ retire the stale journal
```

Therefore the startup virtual-output inspector is not protecting a current production lifecycle path.

## 3.2 `AddonOwnedVirtualDeviceTracker` is never constructed

Current file:

```text
src/SteamInputAddonforClaw/VirtualOutput/Viiper/AddonOwnedVirtualDeviceTracker.cs
```

implements:

```text
IControllerIdentityExclusionSource
```

and maintains instance IDs / uncertain ownership for an older virtual-device classification flow.

Fresh current-main reference review finds no production construction or consumer of `AddonOwnedVirtualDeviceTracker`.

The live Full1902 VIIPER presentation path owns its lifecycle directly and does not feed this tracker.

Therefore keeping this tracker does not add current lifecycle safety; it only keeps an old seam alive.

## 3.3 The classifier's identity-exclusion branch is dead

Current `ControllerDeviceClassifier` still has:

```csharp
private readonly IControllerIdentityExclusionSource _identityExclusionSource;
```

optional constructor parameters, and:

```csharp
if (_identityExclusionSource.IsExcluded(device))
{
    return new ControllerClassificationResult(
        ControllerDeviceClassification.AddonOwnedVirtual,
        "IdentityExclusionSource");
}
```

Current production composition constructs:

```csharp
new ControllerDeviceClassifier(msiClawAdapter.InternalControllerMatcher)
```

with no identity-exclusion source.

Current production startup uses the classifier only through the narrow:

```csharp
classifier.IsInternalHandheld(device, topology)
```

inside `ControllerTopologyWaiter`.

Fresh reference review finds no consumer of:

```text
ControllerDeviceClassification.AddonOwnedVirtual
```

outside the classifier itself.

So the identity-exclusion branch may be removed without altering the real MSI topology waiter.

## 3.4 RecoveryManager still exposes virtual-mutation writers that no production owner calls

Current `RecoveryManager` still exposes:

```text
RecordAddonOwnedVirtualDeviceIntent
ResolveAddonOwnedVirtualDeviceIdentity
CompleteAddonOwnedVirtualDeviceMutation
```

Fresh current-main reference review finds these methods only in:

```text
RecoveryManager itself
RecoveryManagerTests
historical docs/work orders
```

No current `MsiClawAddonPresentation`, `CanonicalViiperRuntime`, physical owner, startup path, or PnP recovery path calls them.

Therefore they are dead write-side API surface.

## 3.5 The legacy schema is a separate product decision

`RecoveryJournal` schema v5 still contains:

```text
AddonOwnedVirtualDeviceRecoveryEntry
RecoveryMutationState.AddonOwnedVirtualDeviceEntries
```

That data may exist in an old development-build `recovery.json`.

The handoff explicitly leaves the product decision between:

```text
Option A — drop old development-build recovery.json compatibility
Option B — bounded one-shot retirement shim
```

Cleanup F must not accidentally make that decision.

For this PR, keep the schema readable/validatable but remove current producers and dead inspectors.

That creates a clean asymmetry intentionally:

```text
legacy field may be read/deserialized
current production never writes it
current production never replays it
```

The dedicated RecoveryJournal cleanup may remove the field/schema later.

---

# 4. Scope boundary

## In scope

1. delete `StartupVirtualOutputRecoveryInspector`;
2. delete `IStartupVirtualOutputRecoveryInspector` and `StartupVirtualOutputRecoveryAssessment`;
3. delete `AddonOwnedVirtualDeviceTracker`;
4. delete `IControllerIdentityExclusionSource` and `EmptyControllerIdentityExclusionSource`;
5. remove the classifier identity-exclusion field/constructor seam/branch;
6. remove `ControllerDeviceClassification.AddonOwnedVirtual` if fresh source/test closure remains empty;
7. remove the three dead RecoveryManager virtual-device mutation writer APIs;
8. remove writer-only helper code that becomes unused, such as `NormalizeIds`;
9. update tests that exist only for these removed seams;
10. keep schema-v5 `AddonOwnedVirtualDeviceEntries` deserialization/validation compatibility for now;
11. zero-residue source/test search for removed symbols.

## Explicitly out of scope

Do not combine Cleanup F with:

```text
M1M2DiagnosticCoordinator deletion/redesign
standalone HidHide whitelist lease cleanup
full RecoveryManager mutation-side deletion
RecoveryJournal schema/version redesign
old recovery.json compatibility decision
StartupHidHideRecoveryCleaner removal
MachineRecoverySafetyInspector removal
DisabledBootControllerAdmission recovery-journal gate removal
RecoverySafetyState changes
PowerTransitionCoordinator incomplete-recovery behavior
HasIncompleteRecovery changes
PID1901/PID1902 ownership changes
HidHide deterministic baseline changes
MsiClawAddonPresentation changes
CanonicalViiperRuntime changes
VIIPER attach/detach/teardown changes
PnP recovery changes
uninstall stock-restoration changes
frontend contracts / protocol
```

This PR should remain a low-risk dead-reference cleanup before the larger recovery cleanup.

---

# 5. Required change A — delete the startup virtual-output recovery inspector leaf

Delete:

```text
src/SteamInputAddonforClaw/Startup/StartupVirtualOutputRecoveryInspector.cs
src/SteamInputAddonforClaw/Startup/IStartupVirtualOutputRecoveryInspector.cs
```

This removes:

```text
IStartupVirtualOutputRecoveryInspector
StartupVirtualOutputRecoveryAssessment
StartupVirtualOutputRecoveryInspector
```

Do not replace it with:

```text
StartupViiperInspector
VirtualOutputRecoveryService
VirtualDeviceReconciliationManager
PresentationRecoveryInspector
```

The current live presentation owner already owns its own lifecycle and teardown.

### Important

Do not change `StartupCoordinator.ResolveStaleRecoveryAsync(...)` merely to compensate for deleting this inspector.

There is currently no production call from that method to the inspector.

Preserve the existing current behavior:

```text
valid old journal
→ clean only validated HidHide residue when required
→ do not replay native/VIIPER state
→ retire journal only after retained cleanup succeeds
```

The broader semantics of journal retirement belong to a later cleanup.

---

# 6. Required change B — remove the dead Addon-owned virtual-device tracker

Delete:

```text
src/SteamInputAddonforClaw/VirtualOutput/Viiper/AddonOwnedVirtualDeviceTracker.cs
```

Fresh review shows no current production owner publishes/resolves/removes identities through this tracker.

Therefore also remove its dead seam:

```text
src/SteamInputAddonforClaw/Controllers/Detection/IControllerIdentityExclusionSource.cs
```

including:

```text
IControllerIdentityExclusionSource
EmptyControllerIdentityExclusionSource
```

Do not keep an always-false implementation merely so an unused classifier constructor can retain its old signature.

Do not create a replacement empty/null object.

---

# 7. Required change C — simplify `ControllerDeviceClassifier` only as far as this dead seam requires

File:

```text
src/SteamInputAddonforClaw/Controllers/Detection/ControllerDeviceClassifier.cs
```

Remove:

```text
_identityExclusionSource
IControllerIdentityExclusionSource constructor parameters
EmptyControllerIdentityExclusionSource fallback
IdentityExclusionSource classification branch
ControllerDeviceClassification.AddonOwnedVirtual
```

Current live construction is matcher-based, so the preferred remaining constructor is conceptually:

```csharp
public ControllerDeviceClassifier(IInternalControllerMatcher internalControllerMatcher)
{
    _internalControllerMatcher = internalControllerMatcher
        ?? throw new ArgumentNullException(nameof(internalControllerMatcher));
}
```

If fresh implementation closure proves another real non-test construction needs a no-matcher form, preserve only the smallest required constructor. Do not preserve it pre-emptively for hypothetical future use.

### Preserve current real behavior

Do not alter in this PR:

```text
IInternalControllerMatcher handling
IsInternalHandheld(...)
internal MSI Claw matching
indeterminate matcher failure handling
known-virtual identification that remains independently referenced
ROOT/VIRTUAL ambiguity behavior if still used
```

In particular, do not simplify `ControllerTopologyWaiter` by bypassing its current internal MSI identity protection.

### No opportunistic full classifier rewrite

Fresh review suggests other general-classifier surface may also have limited current use. Do not turn Cleanup F into a broad `ControllerDeviceClassifier` redesign unless compilation/reference closure mechanically requires it.

The explicit target here is the dead **identity-exclusion / AddonOwnedVirtual** seam.

---

# 8. Required change D — remove the dead RecoveryManager virtual-device write APIs

File:

```text
src/SteamInputAddonforClaw/Recovery/RecoveryManager.cs
```

Delete:

```text
RecordAddonOwnedVirtualDeviceIntent(...)
ResolveAddonOwnedVirtualDeviceIdentity(...)
CompleteAddonOwnedVirtualDeviceMutation(...)
```

Delete helper code that becomes unused only because those writers are gone, currently expected to include:

```text
NormalizeIds(...)
```

Do not add replacement methods to a different owner.

Current Full1902 virtual presentation is not a RecoveryJournal mutation session.

### Preserve read-side legacy validation for this PR

Keep current journal validation capable of accepting a valid schema-v5 `AddonOwnedVirtualDeviceEntries` collection.

Conceptually keep equivalent logic in `IsValidJournal(...)` for old files:

```text
MutationId must be non-empty
DeviceType must be present
stored pre-existing/resolved IDs must be non-empty strings
```

Do not change:

```text
RecoveryManager.CurrentSchemaVersion
RecoveryJournal schema
RecoveryJournalStore serialization format
```

in Cleanup F.

No schema bump is needed because this PR removes writers, not the persisted DTO shape.

---

# 9. Required change E — retain the old virtual journal DTO only as read-only compatibility

File:

```text
src/SteamInputAddonforClaw/Recovery/RecoveryJournal.cs
```

For Cleanup F, retain:

```text
AddonOwnedVirtualDeviceRecoveryEntry
RecoveryMutationState.AddonOwnedVirtualDeviceEntries
```

and retain `HasRecordedMutations` recognition of an old non-empty list.

Why:

```text
old development-build recovery.json may still contain the field
→ current schema-v5 LoadJournal should remain able to validate/deserialise it
→ Cleanup F does not decide whether old recovery.json is supported or discarded
```

### Critical constraint

Do not mistake retaining the DTO for retaining ownership architecture.

After Cleanup F:

```text
DTO may deserialize old evidence
BUT no current production writer creates that evidence
AND no startup inspector uses it to reconstruct/replay a VIIPER device
```

This is intentionally transitional until the dedicated RecoveryJournal cleanup.

---

# 10. Tests — delete stale architecture tests, preserve current lifecycle coverage

## 10.1 `StartupCoordinatorTests`

File:

```text
tests/SteamInputAddonforClaw.Tests/StartupCoordinatorTests.cs
```

Delete stale test doubles that implement the now-deleted inspector:

```text
UnsafeInspector
SequencedVirtualOutputInspector
```

Fresh current code does not inject or invoke them.

Delete or rewrite the stale/misleading test currently named:

```text
VirtualOutputIsRecheckedAfterHidHideCleanupBeforeJournalRetirement
```

Current `StartupCoordinator` performs no virtual-output recheck, so the test name claims behavior that no longer exists.

Preferred handling: delete it if it is redundant with retained journal cleanup tests.

Retain the semantically current test:

```text
MixedJournal_CleansOnlyHidHideResidue_AndNeverReplaysNativeOrVirtualState
```

or equivalent coverage proving:

```text
legacy journal contains native + virtual evidence
→ startup does not replay either
→ only validated HidHide residue is touched
```

Do not introduce a new fake inspector merely to preserve the deleted test structure.

## 10.2 `RecoveryManagerTests`

File:

```text
tests/SteamInputAddonforClaw.Tests/RecoveryManagerTests.cs
```

Delete tests whose only purpose is the removed virtual writer API, including current coverage equivalent to:

```text
VirtualDeviceMutationRoundTripsStructuredIdentity
```

Any current-schema load/round-trip test that currently creates virtual evidence by calling the deleted writer methods should instead construct a schema-v5 journal fixture directly when read-compatibility coverage is still useful.

For example, conceptually:

```csharp
var journal = new RecoveryJournal(
    RecoveryManager.CurrentSchemaVersion,
    Guid.NewGuid(),
    DateTimeOffset.UtcNow,
    originalDeviceState,
    new RecoveryMutationState(
        AddonOwnedVirtualDeviceEntries:
        [
            new AddonOwnedVirtualDeviceRecoveryEntry(
                Guid.NewGuid(),
                "steamdeck",
                0x28DE,
                0x1205,
                [],
                ["USB\\VID_28DE&PID_1205\\legacy"])
        ]));
```

Then verify only that current `LoadJournal()` can read/validate the old schema-v5 shape.

Do not preserve production mutation writers for test convenience.

## 10.3 Classifier tests

Remove/update any test expecting:

```text
AddonOwnedVirtual
IdentityExclusionSource
```

Preserve tests for current meaningful behavior, especially:

```text
internal MSI matcher success
internal matcher exception → Indeterminate where the general classify path is exercised
ControllerTopologyWaiter uses internal MSI identity only
```

Do not add broad new classifier test matrices unrelated to the touched seam.

---

# 11. Explicit lifecycle invariants that must not change

Cleanup F is dead-code/reference cleanup only. The following current production behavior must remain unchanged.

## 11.1 Center M Enabled

```text
Center M roots Enabled
→ topology stabilization
→ stock PID1901 baseline
→ Addon physical controller owner remains inactive
→ resume still verifies the stock baseline
```

## 11.2 Center M Disabled

```text
Center M roots Disabled
→ topology stabilization
→ prerequisite / recovery-journal / HidHide admission
→ Addon physical PID1902 ownership
→ current deterministic HidHide baseline
→ live MsiClawAddonPresentation / VIIPER ownership
```

No deleted tracker/inspector may be replaced in this path.

## 11.3 Sleep / Hibernate / Resume

Preserve current:

```text
PowerMutationGate
PowerTransitionWatcher
suspend barrier / resume validation
stock baseline only for stock authority
Full1902 physical recovery/reconcile for Addon authority
incomplete-recovery fail-close behavior
```

Do not alter power code in this PR.

## 11.4 Physical loss / PnP re-enumeration

Preserve current physical owner handling of:

```text
PID1902 device loss
DirectInput session loss
PnP re-enumeration
PID1902 ↔ PID1901 drift/reclaim
exact owned physical identity
```

Do not use the deleted virtual tracker as a reason to change physical ownership matching.

## 11.5 VIIPER failure / teardown

Preserve current live owner behavior in:

```text
MsiClawAddonPresentation
CanonicalViiperRuntime
current typed X360 / SteamDeck attach/detach
teardown/fail-close behavior
```

The removed tracker is not the live VIIPER owner.

## 11.6 HidHide

Preserve deterministic current-world normalization and readback verification.

Do not reintroduce per-virtual-device or per-application lease ownership as a substitute for the removed seam.

---

# 12. Do not touch the larger RecoveryJournal decision yet

The handoff identifies `RecoveryJournal` as the largest remaining architectural cleanup candidate.

That later cleanup still needs the explicit product decision:

```text
Option A — drop old development-build recovery.json compatibility
Option B — keep one bounded retirement shim
```

Cleanup F must leave that choice open.

Therefore do not remove/reinterpret in this PR:

```text
RecoveryJournal
IRecoveryJournalStore
RecoveryJournalStore
RecoveryManager.LoadJournal
RecoveryManager.HasIncompleteRecovery
StartupHidHideRecoveryCleaner
MachineRecoverySafetyInspector
journal-derived DisabledBoot admission
journal-derived power recovery safety
```

Do not add new migration logic either.

---

# 13. M1/M2 diagnostic is explicitly deferred

Current `M1M2DiagnosticCoordinator` is also unwired production code and is the remaining source consumer of standalone HidHide whitelist-lease APIs such as:

```text
BeginHidHideWhitelistLease
TryGetStandaloneHidHideWhitelistLeaseSessionId
CompleteHidHideWhitelistAddition
```

Do not include that cleanup here.

Reason:

```text
virtual recovery seam deletion = isolated / mechanical
M1M2 coordinator cleanup       = diagnostic + HidHide whitelist lease reference closure
```

Keeping them separate makes review easier and prevents Cleanup F from becoming the full RecoveryManager rewrite.

A later cleanup may delete/sever the diagnostic and then reduce the remaining RecoveryManager mutation-side APIs.

---

# 14. Architecture/reference assertions

After implementation, fresh production source/test search should show zero references to:

```text
StartupVirtualOutputRecoveryInspector
IStartupVirtualOutputRecoveryInspector
StartupVirtualOutputRecoveryAssessment
AddonOwnedVirtualDeviceTracker
IControllerIdentityExclusionSource
EmptyControllerIdentityExclusionSource
ControllerDeviceClassification.AddonOwnedVirtual
RecordAddonOwnedVirtualDeviceIntent
ResolveAddonOwnedVirtualDeviceIdentity
CompleteAddonOwnedVirtualDeviceMutation
```

Historical docs/work orders may retain these names.

Expected retained references include:

```text
AddonOwnedVirtualDeviceRecoveryEntry
RecoveryMutationState.AddonOwnedVirtualDeviceEntries
```

only where required for schema-v5 legacy **read compatibility** and tests that directly validate that compatibility.

There must be no current production code that creates new `AddonOwnedVirtualDeviceRecoveryEntry` values.

---

# 15. No frontend protocol bump

Cleanup F changes internal dead source/test seams only.

It does not change:

```text
FrontendStatusSnapshot
frontend RPC surface
named-pipe DTO shape
settings/bootstrap contract
```

Therefore:

```text
FrontendTransportProtocol.CurrentVersion
```

must remain unchanged.

If implementation unexpectedly requires a frontend DTO change, stop and treat that as scope expansion rather than silently bumping the protocol.

---

# 16. Automated validation

Run:

```text
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Release
git diff --check
```

Also run targeted suites covering at least:

```text
ControllerTopologyWaiterTests
StartupCoordinatorTests
RecoveryManagerTests
MsiClawDeviceAdapterTests
MsiClawAddonPresentationTests
MsiClawAddonPhysicalOwnershipTests
PowerTransitionTests
```

Fresh source/test reference closure:

```text
StartupVirtualOutputRecoveryInspector
IStartupVirtualOutputRecoveryInspector
StartupVirtualOutputRecoveryAssessment
AddonOwnedVirtualDeviceTracker
IControllerIdentityExclusionSource
EmptyControllerIdentityExclusionSource
AddonOwnedVirtual
RecordAddonOwnedVirtualDeviceIntent
ResolveAddonOwnedVirtualDeviceIdentity
CompleteAddonOwnedVirtualDeviceMutation
```

Review the `AddonOwnedVirtual` search manually because historical DTO/type names may still contain the token.

Required production result:

```text
no tracker/inspector/writer authority remains
schema-v5 legacy DTO may remain read-only
```

---

# 17. Manual MSI Claw smoke matrix

Because the intended production behavior is unchanged, do not add production complexity solely to make new hardware tests possible.

If supported hardware is available, a small confidence matrix is sufficient:

## Center M Enabled

```text
boot / runtime startup works
PID1901 stock path works
sleep/resume works
```

## Center M Disabled

```text
boot admission works
PID1902 physical input ownership works
X360 presentation works when Steam/BPM inactive
SteamDeck presentation works when Steam/BPM active
sleep/resume returns to a usable controller presentation
```

If hardware is unavailable, report manual smoke as blocked.

Do not block this dead-code cleanup solely because hardware is unavailable when the complete automated suite and current production reference closure pass.

---

# 18. Review checklist

A reviewer should verify all of the following before merge.

## Dead seam removal

- [ ] `StartupVirtualOutputRecoveryInspector` implementation deleted.
- [ ] `IStartupVirtualOutputRecoveryInspector` deleted.
- [ ] `StartupVirtualOutputRecoveryAssessment` deleted.
- [ ] `AddonOwnedVirtualDeviceTracker` deleted.
- [ ] identity-exclusion source/null-object types deleted.
- [ ] `ControllerDeviceClassification.AddonOwnedVirtual` deleted.
- [ ] classifier no longer has an identity-exclusion branch/constructor dependency.
- [ ] the three virtual RecoveryManager writer APIs are deleted.

## Recovery scope discipline

- [ ] schema v5 remains unchanged.
- [ ] old virtual DTO can still deserialize/validate if retained for compatibility.
- [ ] no current production path writes new virtual recovery evidence.
- [ ] no current startup path replays/reconstructs old virtual evidence.
- [ ] `StartupHidHideRecoveryCleaner` remains intact.
- [ ] `HasIncompleteRecovery` / power fail-close remains intact.
- [ ] DisabledBoot recovery-journal behavior remains intact.

## Real lifecycle safety

- [ ] topology waiter still uses internal MSI identity matching.
- [ ] topology waiter still requires PID1901 or PID1902 control HID and stable snapshots.
- [ ] physical PID1902 ownership/recovery unchanged.
- [ ] HidHide deterministic baseline unchanged.
- [ ] VIIPER presentation/teardown unchanged.
- [ ] sleep/resume behavior unchanged.
- [ ] uninstall/stock restoration unchanged.

## Overengineering guard

- [ ] no replacement tracker/manager/registry/inspector added.
- [ ] no new state/lock/epoch/barrier introduced.
- [ ] no theoretical race hardening bundled into this cleanup.
- [ ] no unrelated classifier rewrite bundled into this cleanup.

---

# 19. Completion criteria

Cleanup F is complete when current production ownership reads approximately as:

```text
physical MSI identity/topology
→ current Full1902 physical owner
→ deterministic HidHide baseline
→ MsiClawAddonPresentation / CanonicalViiperRuntime
```

with no parallel legacy virtual-owner tracker or startup virtual-recovery inspector.

The remaining recovery architecture should then be easier to reason about:

```text
RecoveryJournal schema/readers
+ old HidHide/M1M2 mutation residue
```

rather than:

```text
RecoveryJournal
+ virtual mutation writers
+ virtual tracker
+ classifier exclusion source
+ startup virtual inspector
+ M1M2 lease path
```

That smaller reference closure is the intended setup for the next cleanup.

Do not continue into the next Recovery/M1M2 cleanup in this PR.
