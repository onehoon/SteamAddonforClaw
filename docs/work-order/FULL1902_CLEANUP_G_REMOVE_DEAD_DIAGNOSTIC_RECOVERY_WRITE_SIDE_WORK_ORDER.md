# Work Order — Full1902 Cleanup G: Remove Dead M1/M2 Diagnostic and RecoveryJournal Write-Side

## Status

Focused deletion/simplification work order for removing the remaining **dead diagnostic entry point and routing-era RecoveryJournal mutation/write machinery** after:

```text
PR #476 — Cleanup A: removed legacy Steam-session controller-routing authority
PR #477 — Cleanup B: removed legacy Center M dummy/MainUI suppression subsystem
PR #478 — Cleanup C: removed dead routing-specific power resume branches
PR #479 — Cleanup D: removed controller-software / third-party manager authority
PR #480 — Cleanup E: removed the startup controller-environment authority shell
PR #481 — Cleanup F: removed dead virtual-recovery / identity-exclusion seams
```

This is deliberately **not** the final `RecoveryJournal` removal yet.

Cleanup G closes the write side first:

```text
current production must no longer be capable of creating or mutating recovery.json
```

while preserving the still-current compatibility/read-side behavior for a pre-existing old development-build `recovery.json` until the dedicated final RecoveryJournal decision is implemented.

Code-review baseline used to prepare this work order:

```text
repository: onehoon/SteamAddonforClaw
branch:     main
commit:     1fef665a4a781dc6e691bb9e4b0f3366a9bfda64
latest merged production PR: #481 — Full1902 Cleanup F
```

Before implementation, read and treat these as design authorities:

- `docs/Full 1902 Implementation/README.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/FULL1902_LEGACY_CLEANUP_REVIEW_HANDOFF_2026-09-04.md`
- `docs/work-order/FULL1902_CLEANUP_F_REMOVE_DEAD_VIRTUAL_RECOVERY_IDENTITY_SEAMS_WORK_ORDER.md`

The application is pre-release. Do not preserve routing-era write APIs, lease abstractions, diagnostic contracts, or test-only persistence machinery solely for source compatibility.

---

# 1. Product invariants this cleanup must preserve

There are still exactly two controller-authority modes:

```text
Center M startup roots exactly Enabled
→ MSI / stock controller authority
→ desired physical PID1901
→ no Addon DirectInput/HidHide physical ownership
→ no Addon VIIPER presentation ownership

Center M startup roots exactly Disabled
→ Addon Runtime controller authority
→ desired physical PID1902
→ persistent DirectInput ownership
→ deterministic Addon HidHide baseline
→ one Addon-owned VIIPER runtime
→ exactly one live Xbox360 or SteamDeck presentation
```

Steam/BPM remains presentation-only:

```text
Steam/BPM inactive → Xbox360
Steam/BPM active   → SteamDeck
```

Current Full1902 recovery is current-world reconciliation, not historical mutation replay:

```text
actual current authority / physical / HidHide / VIIPER facts
→ reconcile toward current desired state
→ verify
→ fail closed on actual operation failure
```

Cleanup G must not reintroduce a route-session journal owner or replace deleted recovery writers with a new persistence layer.

---

# 2. Goal

Remove the remaining current-source graph that can still **create or update routing-era recovery journal state**:

```text
M1M2DiagnosticCoordinator
        ↓
standalone HidHide whitelist lease
        ↓
RecoveryManager Begin/Record/Complete mutation APIs
        ↓
IRecoveryJournalStore.WriteNew / ReplaceExisting
        ↓
new or updated recovery.json
```

and remove the diagnostic-only `MsiClawInputSource.Start()` entry path that exists only to support that dead coordinator.

Target end state after Cleanup G:

```text
RecoveryManager
    ├─ HasIncompleteRecovery      ← read-only presence/fail-close fact
    └─ LoadJournal               ← read/validate old schema-v5 evidence only

IRecoveryJournalStore
    ├─ JournalPath
    ├─ Exists
    ├─ ReadText
    └─ Delete                    ← used only for bounded startup retirement

NO production RecoveryJournal writer
NO production journal mutation updater
NO production standalone HidHide lease session
NO M1M2DiagnosticCoordinator
NO diagnostic-only MsiClawInputSource.Start()
```

A pre-existing old `recovery.json` may still be:

```text
found
→ loaded/validated
→ used as bounded stale HidHide ownership evidence by the existing startup cleaner
→ fail closed if invalid/unsafe
→ deleted only through the existing startup retirement path after retained cleanup succeeds
```

That compatibility behavior remains intentionally temporary until the next dedicated RecoveryJournal cleanup.

---

# 3. Current-code proof — why this cleanup is now safe

## 3.1 `M1M2DiagnosticCoordinator` is not part of production composition

Fresh current-main search finds:

```text
src/SteamInputAddonforClaw/Diagnostics/M1M2DiagnosticCoordinator.cs
tests/SteamInputAddonforClaw.Tests/M1M2DiagnosticCoordinatorTests.cs
cleanup documentation
```

but no production/UI construction of `M1M2DiagnosticCoordinator`.

The legacy cleanup handoff already classifies this coordinator as dead production code and explicitly says not to preserve RecoveryManager mutation/lease architecture solely for it.

Therefore delete the coordinator rather than redesigning a new recovery-aware diagnostic.

Future M1/M2 remapping or diagnostics should be rebuilt on current Full1902 input/presentation ownership if needed.

## 3.2 The coordinator is the last live source consumer of standalone HidHide whitelist lease APIs

Current coordinator calls:

```text
BeginHidHideWhitelistLease
TryGetStandaloneHidHideWhitelistLeaseSessionId
CompleteHidHideWhitelistAddition
```

Fresh current-main reference review shows no other production source consumer for these standalone lease APIs.

`OwnsHidHideWhitelistLease` and `GetHidHideWhitelistLeaseSessionId` have no production callers at all.

Once the coordinator is deleted, the entire standalone whitelist-lease API surface is dead.

## 3.3 Routing-era native/HidHide mutation writers also have no production caller

Fresh current-main review shows these methods are referenced only by `RecoveryManager` itself, tests, or historical work orders:

```text
BeginDeviceNativeStateMutation
RecordHidHideWhitelistAddition
RecordHidHideDeviceAddition
RecordHidHideActiveStateMutation
CompleteHidHideActiveStateMutation
CompleteDeviceNativeStateMutation
CompleteHidHideDeviceAddition
```

Current Full1902 physical ownership is explicitly forbidden from using the old route-mutation APIs, and existing architecture tests already assert that the new PID1902 owner does not call them.

Therefore Cleanup G may remove the complete mutation write/update side instead of keeping isolated unused methods.

## 3.4 `RecoveryJournalStore.WriteNew` / `ReplaceExisting` exist only for the dead write side

Current production `WriteNew(...)` calls are inside `RecoveryManager` mutation/session creation.

Current production `ReplaceExisting(...)` calls are inside `RecoveryManager.UpdateRecoverySession(...)`.

After the mutation APIs are removed, there is no production reason for `IRecoveryJournalStore` to expose write/replace operations.

Tests must not keep production persistence APIs alive merely to manufacture fixtures.

## 3.5 Current read-side consumers remain real until the final compatibility decision

Current production still consumes journal presence/read state in real gates including:

```text
StartupCoordinator.ResolveStaleRecoveryAsync
DisabledBootControllerAdmission via RecoveryManager.LoadJournal
AddonRuntimeHost / PowerTransitionCoordinator via HasIncompleteRecovery
MachineRecoverySafetyInspector / prerequisite safety
other journal-derived termination/setup fail-close paths found by fresh closure
```

These are not removed in Cleanup G.

The point of this PR is to make the journal **legacy input only**, not to decide yet whether that input remains supported.

---

# 4. Scope boundary

## In scope

1. delete `M1M2DiagnosticCoordinator` and its dedicated test file;
2. delete `IMsiClawInputDiagnostic` if fresh closure remains limited to the coordinator/source/tests;
3. remove the diagnostic-only `MsiClawInputSource.Start()` path;
4. remove helpers/enum values used only by that diagnostic `Start()` path when fresh closure is zero;
5. remove all dead RecoveryManager mutation / lease / completion APIs;
6. remove private RecoveryManager update/session helpers that become unreachable;
7. remove `IRecoveryJournalStore.WriteNew(...)` and `ReplaceExisting(...)`;
8. remove the corresponding production implementations from `RecoveryJournalStore`;
9. rewrite tests to construct old journal fixtures without production write APIs;
10. preserve old schema-v5 read/validation compatibility for this PR;
11. preserve `Delete()` because current startup retirement still uses it;
12. zero-residue source/test search for removed write-side symbols.

## Explicitly out of scope

Do not combine Cleanup G with:

```text
final RecoveryJournal deletion
old recovery.json Option A / Option B decision
RecoveryJournal schema-version removal
StartupHidHideRecoveryCleaner removal/redesign
StartupCoordinator stale-journal retirement redesign
DisabledBootControllerAdmission recovery-journal gate removal
MachineRecoverySafetyInspector removal
UserTerminationGuard journal/recovery safety changes
RecoverySafetyState removal
PowerTransitionCoordinator incomplete-recovery changes
PID1901/PID1902 physical ownership changes
MsiClawAddonPhysicalOwnership changes
MsiClawAddonPresentation changes
CanonicalViiperRuntime changes
HidHide deterministic baseline changes
Center M startup-root authority changes
sleep/hibernate/resume behavior changes
PnP recovery changes
uninstall stock restoration changes
frontend contracts/protocol
broad MsiClawInputSource polling redesign
MsiClawInputTestSummary rename/redesign
```

Cleanup G should close the writer graph without forcing the final legacy-file policy decision.

---

# 5. Required change A — delete the dead M1/M2 diagnostic coordinator

Delete:

```text
src/SteamInputAddonforClaw/Diagnostics/M1M2DiagnosticCoordinator.cs
tests/SteamInputAddonforClaw.Tests/M1M2DiagnosticCoordinatorTests.cs
```

Do not replace it with:

```text
M1M2DiagnosticService
DiagnosticWhitelistLease
DiagnosticRecoveryManager
ControllerDiagnosticOwner
temporary HidHide manager
```

The feature is currently unwired. Deleting the dead implementation is preferable to preserving production recovery ownership solely for it.

### Preserve future product direction

This deletion does **not** remove M1/M2 input support from current Full1902 controller state mapping.

Do not remove:

```text
MsiClawControls.M1 / M2
DirectInput button mapping
ControllerState auxiliary-button state
current physical input publication
future M1/M2 remapping capability
```

Only the obsolete standalone diagnostic workflow is being removed.

---

# 6. Required change B — remove the diagnostic-only MsiClawInputSource entry contract

Files:

```text
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputContracts.cs
src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawInputSource.cs
```

## 6.1 Delete `IMsiClawInputDiagnostic`

Current interface:

```csharp
internal interface IMsiClawInputDiagnostic : IMsiClawPreparedInputSource
{
    event EventHandler<MsiClawInputTestSummary>? TestCompleted;
    MsiClawInputStartResult Start();
}
```

Fresh review shows the interface exists to support `M1M2DiagnosticCoordinator` and its tests.

Delete it.

`MsiClawInputSource` should continue to implement the current prepared/live input contract needed by Full1902 ownership.

Do not add a replacement diagnostic interface.

## 6.2 Delete `MsiClawInputSource.Start()`

Current parameterless `Start()` performs its own DirectInput enumeration/selection and logs:

```text
M1/M2 input diagnostic requested
AbortDiagnostic
DiagnosticAlreadyRunning
```

That path is diagnostic-only.

Current Full1902 ownership uses the prepared path:

```text
physical owner resolves/validates exact DirectInput descriptor
→ MsiClawInputSource.StartPrepared(descriptor)
```

Delete the parameterless `Start()` and any helper used only by it, expected to include after fresh closure:

```text
LogCandidates(...)
MapSelectionFailure(...)
```

Do not weaken `StartPrepared(...)` descriptor verification or acquisition behavior.

## 6.3 Remove diagnostic-only start status values only if zero references remain

After deleting the parameterless Start path, fresh closure is expected to make these values dead:

```text
MsiClawInputStartStatus.EnumerationFailed
MsiClawInputStartStatus.Pid1902NotFound
```

Delete them only if source/test search confirms no retained current path consumes them.

Keep current status values still used by `StartPrepared(...)` and real physical ownership, including as applicable:

```text
Started
AlreadyRunning
InitializationFailed
Indeterminate
CreateDeviceFailed
AcquireFailed
```

Do not redesign the whole start-result contract in this PR.

## 6.4 Preserve the live physical-session completion event

This is critical.

Although the names originated in diagnostics, current production `AddonProcessHost` subscribes to:

```text
MsiClawInputSource.TestCompleted
```

and uses `MsiClawInputTestSummary.StopReason` to classify actual owned DirectInput session loss and schedule Full1902 recovery.

Therefore **do not delete** in Cleanup G:

```text
MsiClawInputTestSummary
MsiClawInputStopReason
MsiClawInputSource.TestCompleted
polling completion notification
read-failure / invalid-layout / initial-state-not-ready stop classification
```

Do not rename this live contract merely because the name contains `Test`.

A naming cleanup may be considered later, but it must not be mixed into recovery-writer deletion.

## 6.5 Optional dead event cleanup

Fresh current-main search shows `IndependentVerified` has no source/test consumer outside `MsiClawInputSource` itself.

It may be removed mechanically if still unreferenced after deleting the diagnostic coordinator.

Do **not** use this as a reason to remove M1/M2 state mapping, summary fields, or physical polling logic in this PR.

---

# 7. Required change C — remove the entire RecoveryManager write/mutation API surface

File:

```text
src/SteamInputAddonforClaw/Recovery/RecoveryManager.cs
```

Delete the dead creation/lease methods:

```text
BeginDeviceNativeStateMutation(...)
BeginHidHideWhitelistLease(...)
OwnsHidHideWhitelistLease(...)
GetHidHideWhitelistLeaseSessionId(...)
TryGetStandaloneHidHideWhitelistLeaseSessionId(...)
```

Delete the dead mutation-recording methods:

```text
RecordHidHideWhitelistAddition(...)
RecordHidHideDeviceAddition(...)
RecordHidHideActiveStateMutation(...)
```

Delete the dead mutation-completion methods:

```text
CompleteHidHideActiveStateMutation(...)
CompleteDeviceNativeStateMutation(...)
CompleteHidHideWhitelistAddition(...)
CompleteHidHideDeviceAddition(...)
```

Delete private helpers that become unreachable, expected to include:

```text
TryNormalizeDeviceEntry(...)
UpdateRecoverySession(...)
BeginRecoverySession(...)
```

Fresh closure must determine the exact final list; do not retain dead helpers for old tests.

### Target RecoveryManager shape

Conceptually, after Cleanup G it should be approximately:

```csharp
internal sealed class RecoveryManager(IRecoveryJournalStore store)
{
    internal const int CurrentSchemaVersion = 5;

    public bool HasIncompleteRecovery { get; }

    public RecoveryResult LoadJournal()
    {
        // current schema-v5 legacy read/validation only
    }

    private static bool IsValidJournal(RecoveryJournal journal)
    {
        // retained old-file validation
    }
}
```

Exact syntax may differ, but there must be no create/update/lease authority left.

### Do not rename/wrap RecoveryManager in this PR

Although the remaining role becomes read-only and the name `RecoveryManager` is broader than the behavior, do not introduce:

```text
RecoveryJournalReader
LegacyRecoveryFacade
RecoveryCompatibilityManager
JournalInspector
```

solely for architectural cosmetics.

The final RecoveryJournal cleanup may remove the type entirely after the old-file policy is decided.

---

# 8. Required change D — make `IRecoveryJournalStore` read/delete only

File:

```text
src/SteamInputAddonforClaw/Recovery/RecoveryJournalStore.cs
```

Current contract:

```csharp
string JournalPath { get; }
bool Exists();
string ReadText();
void WriteNew(RecoveryJournal journal);
void ReplaceExisting(RecoveryJournal journal);
void Delete();
```

Target contract:

```csharp
string JournalPath { get; }
bool Exists();
string ReadText();
void Delete();
```

Delete production implementations of:

```text
WriteNew
ReplaceExisting
```

and writer-only serialization/temp-file code that becomes unused.

This is an important architectural assertion:

> After Cleanup G, current production code has no API that can create or update `recovery.json`.

Keep `Delete()` because current `StartupCoordinator` still retires a validated stale journal after retained cleanup succeeds.

Do not change the journal path in this PR.

---

# 9. Required change E — keep schema-v5 legacy read compatibility intact

File:

```text
src/SteamInputAddonforClaw/Recovery/RecoveryJournal.cs
```

Cleanup G does **not** make the final Option A / Option B decision.

Retain the persisted DTO fields needed to deserialize current schema-v5 old files:

```text
RecoveryJournal.SchemaVersion
RecoverySessionId
CreatedAt
OriginalDeviceState
RecoveryMutationState
    DeviceNativeStateChanged
    HidHideDeviceAdditions
    ExecutableWhitelistAdditions
    AddonOwnedVirtualDeviceEntries
    OriginalHidHideActiveState
AddonOwnedVirtualDeviceRecoveryEntry
```

Retain:

```text
RecoveryManager.CurrentSchemaVersion == 5
LoadJournal schema check
IsValidJournal old-file validation
```

Do not bump the schema.

Do not add migration code.

Do not reinterpret old fields as current Full1902 ownership.

### Dead helper properties

If fresh closure shows helper properties such as:

```text
RecoveryMutationState.HasRecordedMutations
RecoveryResult.IsSafeToContinue
```

are now unused, they may be deleted only if doing so does not require altering the persisted constructor-field compatibility above.

This is optional mechanical cleanup; do not broaden into a DTO redesign.

---

# 10. Existing stale-journal startup behavior must remain unchanged

File:

```text
src/SteamInputAddonforClaw/Startup/StartupCoordinator.cs
```

Do not redesign `ResolveStaleRecoveryAsync(...)` in Cleanup G.

Preserve the existing behavior:

```text
no journal
→ continue

journal load/validation failure
→ RecoverySafe=false / fail closed
→ preserve file

valid old journal with HidHide evidence
→ StartupHidHideRecoveryCleaner only
→ no native/VIIPER replay
→ cleanup must succeed and verify

valid journal after required cleanup
→ delete through IRecoveryJournalStore.Delete()
→ verify absence
→ RecoverySafe=true
```

The fact that current production can no longer create a journal does not mean startup may blindly ignore an already-existing old file.

That decision belongs to the next cleanup.

---

# 11. Disabled boot journal gate remains intact

Current `AddonStartupComposition` wires:

```csharp
new DisabledBootControllerAdmission(
    prerequisiteInspector,
    runtimeRecoveryManager.LoadJournal,
    normalizeHidHideBaseline)
```

Preserve this in Cleanup G.

Current Disabled-mode startup must still fail closed if a pre-existing stale journal is present/invalid according to the current admission contract.

Do not bypass the journal gate merely because new journals can no longer be written.

The final RecoveryJournal compatibility cleanup may remove this gate once the old-file policy is decided.

---

# 12. Runtime / power incomplete-recovery gate remains intact

Current runtime composition uses:

```text
RecoveryManager.HasIncompleteRecovery
→ AddonRuntimeHost
→ PowerTransitionCoordinator
```

Preserve the current fail-close behavior on resume while a legacy journal still exists.

Do not remove:

```text
HasIncompleteRecovery
RecoverySafetyState
PowerTransitionCoordinator incomplete-recovery check
stock PID1901 resume baseline
Full1902 post-resume recovery
```

Cleanup G only proves that **new current-process work cannot create recovery journal state**.

The next cleanup can use that proof when deciding whether journal presence should still affect power/runtime safety.

---

# 13. Machine/setup recovery safety remains intact

Do not remove or simplify in Cleanup G:

```text
MachineRecoverySafetyInspector
ElevatedPrerequisiteSetup recovery-safety gate
any current setup/termination guard that treats recovery.json presence as unsafe
```

After Cleanup G these components only observe old/pre-existing journal state, but that is still the current compatibility policy.

Removing those observers belongs to the final RecoveryJournal closure.

---

# 14. Tests — remove writer architecture tests, preserve read/fail-close coverage

## 14.1 Delete M1M2 coordinator tests

Delete:

```text
tests/SteamInputAddonforClaw.Tests/M1M2DiagnosticCoordinatorTests.cs
```

Do not rebuild the old HidHide lease state machine in test code.

## 14.2 Update `MsiClawInputSourceTests`

Delete tests whose only production target is the removed parameterless diagnostic:

```text
source.Start()
```

Expected examples include diagnostic-only enumeration / PID1902-not-found / diagnostic initialization cases.

Preserve or move meaningful lower-level coverage to existing current owners where applicable:

```text
MsiClawDirectInputDeviceSelector tests
StartPrepared descriptor verification
CreateDevice / Acquire failure
polling/state publication
first valid state
StopAsync / DisposeAsync
TestCompleted / StopReason behavior
read failure
invalid layout
known-invalid initial-state handling
physical identity/session generation behavior
```

Do not delete real polling/recovery tests just because they share code with the former diagnostic path.

## 14.3 Rewrite `RecoveryManagerTests` around read-only behavior

Delete tests whose only target is removed writer/update APIs.

Examples expected to disappear:

```text
BeginDeviceNativeStateMutation_Persists...
UnsafeCapture_DeniesMutation
HidHideLease_DoesNotCreateDeviceSnapshot
WriteFailure_DeniesNativeStateMutation
SecondBegin_DoesNotOverwriteCrashEvidence
NativeThenWhitelistRecordUsesSameSession
RecordDeviceAdditionPreservesNativeAndWhitelist
WrongSessionCannotRecord...
CompleteDeviceAddition...
MutationCompletion...
StandaloneWhitelistBegin...
```

Keep/rebuild only current read compatibility and fail-close coverage using direct fixture files, for example:

```text
no file → NoRecoveryNeeded
valid schema-v5 journal → Success
legacy AddonOwnedVirtualDeviceEntries fixture → still validates
mixed native/HidHide/virtual old evidence → loads as old evidence
unsupported schema → Failure and file preserved
malformed JSON → Failure and file preserved
invalid required schema-v5 state → Failure
Exists/read exception → fail closed
HasIncompleteRecovery reflects presence / inspection failure safely
```

### Important test-fixture rule

Do not keep `WriteNew`/`ReplaceExisting` on the production store just to manufacture tests.

Tests may create `recovery.json` directly with:

```text
File.WriteAllText
JsonSerializer.Serialize
small test-only fixture helpers
```

The test helper must not become production source.

## 14.4 Update `RecoveryJournalStoreTests`

Remove tests for deleted production write/replace behavior.

Retain focused coverage for the remaining contract where useful:

```text
Exists
ReadText
Delete
JournalPath
Delete idempotence / normal File.Delete semantics if currently meaningful
```

## 14.5 Update fake stores across tests

All fake `IRecoveryJournalStore` implementations must drop:

```text
WriteNew
ReplaceExisting
```

Preserve read/delete behavior required by:

```text
StartupCoordinatorTests
UnsupportedHardwareStartupGateTests
other current startup/recovery safety tests
```

Do not add no-op writer methods back through a new interface.

---

# 15. Architecture/reference assertions

After implementation, fresh source/test search should show zero references to:

```text
M1M2DiagnosticCoordinator
IMsiClawInputDiagnostic
BeginHidHideWhitelistLease
OwnsHidHideWhitelistLease
GetHidHideWhitelistLeaseSessionId
TryGetStandaloneHidHideWhitelistLeaseSessionId
RecordHidHideWhitelistAddition
RecordHidHideDeviceAddition
RecordHidHideActiveStateMutation
CompleteHidHideActiveStateMutation
CompleteDeviceNativeStateMutation
CompleteHidHideWhitelistAddition
CompleteHidHideDeviceAddition
UpdateRecoverySession
BeginRecoverySession
IRecoveryJournalStore.WriteNew
IRecoveryJournalStore.ReplaceExisting
```

Historical docs/work orders may retain these terms.

If the diagnostic `Start()` is removed, source/test search should also show no parameterless MsiClaw input start path.

Expected retained current production references include:

```text
RecoveryManager.LoadJournal
RecoveryManager.HasIncompleteRecovery
RecoveryManager.CurrentSchemaVersion
RecoveryJournal / RecoveryMutationState schema DTOs
IRecoveryJournalStore.Exists
IRecoveryJournalStore.ReadText
IRecoveryJournalStore.Delete
StartupHidHideRecoveryCleaner
DisabledBootControllerAdmission journal read gate
PowerTransition incomplete-recovery gate
MsiClawInputSource.StartPrepared
MsiClawInputSource.TestCompleted
MsiClawInputTestSummary / StopReason
```

---

# 16. Explicit lifecycle invariants that must not change

Cleanup G is architecture cleanup, not a lifecycle redesign.

## 16.1 Center M Enabled

```text
startup roots Enabled
→ topology stabilization
→ stock PID1901 baseline
→ Addon physical owner inactive
→ resume still verifies stock baseline
```

## 16.2 Center M Disabled

```text
startup roots Disabled
→ topology stabilization
→ prerequisite / legacy-journal / HidHide admission
→ PID1902 ownership
→ exact deterministic HidHide baseline
→ one live VIIPER presentation
```

## 16.3 Sleep / Hibernate / Resume

Preserve:

```text
PowerMutationGate
suspend barrier
resume epoch validation
current participant quiesce behavior
stock authority baseline only in Enabled mode
Full1902 physical recovery in Disabled mode
legacy journal presence fail-close until next cleanup
```

Do not add locks/epochs/barriers for theoretical races.

## 16.4 Physical device loss / PnP re-enumeration

Preserve current:

```text
DirectInput session loss detection
MsiClawInputSource.TestCompleted stop reason
physical recovery scheduling
PID1902/PID1901 drift classification and reclaim
exact physical identity handling
```

Removing the M1/M2 diagnostic must not remove the live completion event used by this path.

## 16.5 HidHide

Preserve current deterministic normalization and readback verification.

Cleanup G removes an obsolete **temporary diagnostic whitelist lease**, not the persistent Full1902 Addon authority baseline.

Do not alter:

```text
Official HidHideCLI registration
Official HidHideClient registration
current Addon runtime registration
exact owned PID1902 hidden target
Inverse=false
Active=true while Addon authority is active
```

## 16.6 VIIPER

No changes to:

```text
CanonicalViiperRuntime
MsiClawAddonPresentation
Xbox360/SteamDeck switching
attach/detach/teardown
rumble routing
presentation recovery
```

---

# 17. No frontend protocol bump

Cleanup G changes internal dead source/test and recovery persistence surface only.

It does not change:

```text
FrontendStatusSnapshot
frontend RPCs
named-pipe DTOs
settings transport
```

Therefore do not bump:

```text
FrontendTransportProtocol.CurrentVersion
```

unless implementation unexpectedly changes a frontend contract, in which case treat that as scope expansion rather than silently including it.

---

# 18. Why the final RecoveryJournal cleanup is intentionally next, not part of G

After Cleanup G, the architecture should prove:

```text
current product code cannot create recovery.json
current product code cannot update recovery.json
current product code cannot create standalone journal lease/mutation sessions
```

Therefore any file found at startup is necessarily:

```text
legacy development-build residue
or externally/manual-created/corrupt state
```

That clean fact makes the next decision much simpler.

The next dedicated cleanup can choose explicitly between:

```text
Option A — pre-release: drop old recovery.json compatibility entirely

Option B — one bounded legacy retirement shim
```

without needing to preserve a mutation writer for current runtime behavior.

Do not implement that decision in Cleanup G.

---

# 19. Automated validation

Run:

```text
dotnet build -c Debug
dotnet build -c Release
dotnet test -c Release
git diff --check
```

Run focused suites for at least:

```text
MsiClawInputSourceTests
MsiClawAddonPhysicalOwnershipTests
MsiClawAddonPresentationTests
StartupCoordinatorTests
DisabledBootControllerAdmissionTests
RecoveryManagerTests
RecoveryJournalStoreTests
PowerTransitionTests
MachineRecoverySafetyInspectorTests
AddonProcessHostStartupTests
```

Fresh source/test reference closure for all symbols listed in section 15.

Also confirm explicitly:

```text
no production source calls IRecoveryJournalStore.WriteNew / ReplaceExisting
no production source creates a RecoveryJournal for current runtime mutation ownership
no production M1M2DiagnosticCoordinator construction exists
MsiClawInputSource.TestCompleted remains wired in AddonProcessHost
StartPrepared remains the Full1902 physical input start path
```

Historical docs may retain removed terms.

---

# 20. Manual MSI Claw smoke matrix

If supported hardware is available, a small unchanged-behavior smoke pass is sufficient.

## Center M Enabled

```text
boot reaches stock PID1901
normal device/TDP features remain usable
sleep/resume returns to usable stock controller
```

## Center M Disabled

```text
boot admission succeeds when no legacy recovery.json exists
PID1902 physical input starts through StartPrepared
Xbox360 presentation works with Steam/BPM inactive
SteamDeck presentation works with Steam/BPM active
M1/M2 still appear in normal ControllerState mapping as before
physical input loss still schedules recovery
sleep/resume returns to a usable presentation
```

## Legacy recovery.json fixture

If practical in a development environment:

```text
valid old schema-v5 journal
→ existing startup read/cleanup/retirement behavior still works

invalid/malformed old journal
→ still fails closed / remains preserved
```

If hardware is unavailable, report hardware smoke as blocked. Do not add production complexity solely to simulate it.

---

# 21. Review checklist

## Dead diagnostic

- [ ] `M1M2DiagnosticCoordinator` deleted.
- [ ] dedicated coordinator tests deleted.
- [ ] `IMsiClawInputDiagnostic` deleted if no retained caller exists.
- [ ] parameterless `MsiClawInputSource.Start()` diagnostic path deleted.
- [ ] `StartPrepared(...)` remains intact.
- [ ] current M1/M2 ControllerState mapping remains intact.
- [ ] `TestCompleted` / `MsiClawInputTestSummary.StopReason` live recovery contract remains intact.

## Recovery write side

- [ ] no RecoveryManager begin/record/complete mutation APIs remain.
- [ ] no standalone HidHide whitelist lease API remains.
- [ ] no `UpdateRecoverySession` / `BeginRecoverySession` write helper remains.
- [ ] `IRecoveryJournalStore.WriteNew` removed.
- [ ] `IRecoveryJournalStore.ReplaceExisting` removed.
- [ ] production `RecoveryJournalStore` contains no journal creation/update serializer path.
- [ ] current production cannot create or update recovery.json.

## Retained legacy read safety

- [ ] `LoadJournal` remains.
- [ ] `HasIncompleteRecovery` remains.
- [ ] schema-v5 DTO fields remain readable.
- [ ] unsupported/malformed journals still fail closed.
- [ ] `StartupHidHideRecoveryCleaner` remains intact.
- [ ] startup delete/verify retirement remains intact.
- [ ] DisabledBoot journal gate remains intact.
- [ ] power/runtime incomplete-journal fail-close remains intact.
- [ ] MachineRecoverySafetyInspector remains intact.

## Real lifecycle safety

- [ ] Center M Enabled stock baseline unchanged.
- [ ] Center M Disabled PID1902 ownership unchanged.
- [ ] HidHide deterministic baseline unchanged.
- [ ] PnP/session-loss recovery unchanged.
- [ ] VIIPER ownership/teardown unchanged.
- [ ] sleep/hibernate/resume unchanged.
- [ ] uninstall stock restoration unchanged.

## Overengineering guard

- [ ] no replacement journal writer/lease service added.
- [ ] no new recovery facade/manager/state machine added.
- [ ] no new lock/epoch/barrier for theoretical races.
- [ ] no broad polling/input-source redesign bundled into this cleanup.
- [ ] no final old-recovery.json policy silently chosen in this PR.

---

# 22. Completion criteria

Cleanup G is complete when the source architecture reads approximately as:

```text
Current Full1902 runtime
    → never writes recovery.json
    → never updates recovery.json
    → never owns a journal mutation/lease session

Old recovery.json, if present
    → RecoveryManager.LoadJournal only
    → existing bounded stale HidHide cleanup / fail-close policy
    → existing verified delete retirement

Current controller ownership
    Center M Enabled  → stock PID1901
    Center M Disabled → PID1902 + deterministic HidHide + VIIPER

Current DirectInput source
    → StartPrepared(exact verified descriptor)
    → TestCompleted still reports real physical-session completion
```

No current production behavior should depend on the removed M1/M2 diagnostic or any RecoveryJournal write-side abstraction.

The next cleanup should then make the explicit final decision for old `recovery.json` compatibility and close the remaining read-side journal-derived gates in one evidence-based pass.
