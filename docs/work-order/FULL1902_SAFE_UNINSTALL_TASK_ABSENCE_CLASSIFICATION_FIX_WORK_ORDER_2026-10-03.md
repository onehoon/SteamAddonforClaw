# Work Order — Full1902 Safe Uninstall Task-Absence Classification Fix

## Status

Focused production bug-fix work order for the Full1902 safe-uninstall path.

This work order is based on:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/work-order/PR12_STOCK_SAFE_UNINSTALL_CORE_WORK_ORDER.md`
- `docs/work-order/PR13_FULL1902_SAFE_UNINSTALL_ENTRY_DEPENDENCY_AND_CLAWHUD_CLEANUP_WORK_ORDER_2026-09-27.md`
- current `main` implementation of `Install/StartupRegistration.cs`
- hardware log evidence from `GoogleDrive/Addon/Log/1003` captured on 2026-10-03

The application is standalone Full1902. CTW integration is out of scope.

---

## 1. Goal

Fix the real safe-uninstall failure where the Addon-owned Windows startup task is successfully deleted, but the following Task Scheduler read-back reports the missing task as:

```text
System.IO.FileNotFoundException
HRESULT = 0x80070002
```

instead of the `COMException(0x80070002)` shape currently recognized by `WindowsOwnedStartupTaskStore.Read()`.

The product must interpret **only the exact missing-task HRESULT** as verified absence regardless of whether the COM/dynamic interop layer surfaces it as:

- `COMException`, or
- `FileNotFoundException`.

All other read failures must continue to fail closed.

This is a narrow exception-classification fix. Do not redesign the uninstall protocol, controller authority model, Task Scheduler ownership, or Runtime lifetime.

---

## 2. Production evidence

The 2026-10-03 hardware log proves the Full1902 stock-restoration portion succeeded before uninstall was blocked.

Observed sequence:

```text
08:30:17.581
CenterMState=Enabled

08:30:17.651
Live Stock MSI Claw baseline is already XInput.
ModeWrite=False

08:30:17.672
HidHide Enabled-mode baseline already compliant.
Active=False

08:30:17.957
Center M Enable verified.
FinalState=Enabled

08:30:17.964
Startup task elevated removal requested.

08:30:18.044
Startup task elevated removal completed.
ElevatedRepairResult=Removed
```

The failure begins only during the independent read-back:

```text
System.IO.FileNotFoundException: 지정된 파일을 찾을 수 없습니다. (0x80070002)
    at ... rootFolder.GetTask(...)
    at SteamInputAddonforClaw.Install.WindowsOwnedStartupTaskStore.Read()
```

The bounded absence verification consequently ends with:

```text
Startup task removal readback verification completed.
ReadbackVerified=False

Stock uninstall preparation completed.
Outcome=Failed
Reason="StartupTaskRemoval:Windows startup setting could not be applied."

Uninstall stock preparation did not succeed; Runtime will remain active.
```

The external safe-uninstall process then correctly waits for the Runtime mutex and times out after 60 seconds because the Runtime deliberately remained alive:

```text
Running Runtime did not release its mutex within the safe-uninstall wait budget.
WaitBudgetMs=60000
```

Therefore:

> The 60-second mutex wait is not the root cause in this incident.

Increasing the wait budget would only delay the same failure.

---

## 3. Root cause

Current `WindowsOwnedStartupTaskStore.Read()` recognizes a missing task only through:

```csharp
catch (COMException exception)
    when (exception.HResult == FileNotFoundHResult)
{
    return null;
}
```

The production Windows Task Scheduler COM call is invoked through `dynamic`.

On the tested MSI Claw / Windows build, the interop binder surfaces the same missing-task HRESULT as:

```text
System.IO.FileNotFoundException
HResult = 0x80070002
```

That exception falls into the generic catch, is logged as a genuine read failure, and is rethrown.

The higher-level `TryRead()` intentionally distinguishes:

```text
successful read + null
    = verified task absence

exception during read
    = unverified failure
```

That fail-closed contract is correct and must remain.

The bug is lower-level: one legitimate representation of the exact Task Scheduler "task not found" condition is incorrectly classified as a read failure.

---

## 4. Required implementation

### 4.1 Fix `WindowsOwnedStartupTaskStore.Read()`

Update the missing-task handling so the exact `0x80070002` condition is recognized when surfaced as either `COMException` or `FileNotFoundException`.

Acceptable minimal shape:

```csharp
catch (COMException exception)
    when (exception.HResult == FileNotFoundHResult)
{
    return null;
}
catch (FileNotFoundException exception)
    when (exception.HResult == FileNotFoundHResult)
{
    return null;
}
```

Apply this to the effective exception boundary around `GetTask(...)` / `Read()` so both:

- initial startup-task reads, and
- post-delete absence verification

observe the same missing-task semantics.

Do not catch every `FileNotFoundException` unconditionally.

The HRESULT guard is required.

### 4.2 Preserve genuine read failures

Do not weaken:

```csharp
TryRead(out state)
```

into "any exception means absent."

The following must still be failures:

- access denied;
- Task Scheduler service/connect failure;
- COM/binder failure with any other HRESULT;
- malformed task-definition access;
- unexpected runtime exceptions;
- any `FileNotFoundException` whose HRESULT is not the exact missing-task contract.

PR12's invariant remains:

> A genuine read failure must never be mistaken for verified absence.

---

## 5. Required uninstall behavior after the fix

For the production sequence captured on 2026-10-03:

```text
Center M already Enabled
+ PID1901/XInput already proven
+ HidHide already released
+ Addon startup task exists
```

the expected path becomes:

```text
elevated exact-task delete
→ child reports Removed
→ normal Runtime independently reads task
→ GetTask reports 0x80070002
→ Read() returns null
→ ReadBackVerified=True
→ startup-task removal succeeds
→ StockUninstallPrepareResult.Succeeded=True
→ Runtime exits
→ mutex releases
→ safe-uninstall process continues its independent stock-safety proof
→ final dependency / Velopack uninstall proceeds
```

Do not bypass the independent read-back.

Do not trust the elevated helper's `Removed` result alone.

---

## 6. Tests

Update/add focused tests around the production exception shape.

### 6.1 Required unit coverage

At minimum prove:

1. `COMException(0x80070002)` still means task absent.
2. `FileNotFoundException` with `HResult=0x80070002` means task absent.
3. Another `FileNotFoundException`/exception shape does **not** become absence.
4. `Synchronize(false)` succeeds when the post-delete read-back observes the exact production missing-task condition.
5. Existing tests that require failure on a genuine post-delete read error continue to pass.

If directly constructing the exact framework exception/HRESULT in a test is awkward, add the smallest test seam needed around the task-store read classification. Do not introduce a new Task Scheduler abstraction hierarchy solely for this test.

### 6.2 Preserve existing safety tests

The following existing policies must remain covered:

- already-absent task = read-only success;
- elevated child claims Removed but task remains present = failure;
- pre-delete read failure = failure;
- post-delete genuine read failure = failure;
- direct elevated-child delete requires verified absence.

---

## 7. Logging

Keep current lifecycle logs.

The fixed hardware path should show approximately:

```text
Startup task elevated removal completed. ElevatedRepairResult=Removed
Startup task removal readback verification completed. ReadbackVerified=True
Stock uninstall startup task removal. Outcome=Succeeded
Stock uninstall preparation completed. Outcome=Succeeded
Uninstall stock preparation succeeded.
Runtime process cleanup completed.
```

Do not add noisy repeated warning logs for the expected exact missing-task condition.

A missing owned task after delete is a success fact, not an error/warning.

---

## 8. Explicit non-goals

Do **not** include any of the following in this PR:

- increasing `SafeUninstall.RuntimeReleaseBudget` from 60 seconds;
- adding retries beyond the existing bounded read-back settle;
- changing Center M authority transition ordering;
- changing PID1901/PID1902 logic;
- changing HidHide release policy;
- introducing an uninstall state machine;
- adding a new manager/service/broker;
- genericizing the startup-task helper;
- removing the independent post-elevation read-back;
- propagating the detailed Runtime preparation failure reason to the outer uninstaller UI;
- redesigning `SingleInstanceGate`;
- modifying VeloPack cleanup policy.

The current outer error message is less diagnostic than the Runtime log, but that is a separate UX improvement and is not required to fix this production blocker.

---

## 9. Do not add the previously suspected Center M optimization here

The 2026-10-03 log also shows that uninstall calls `SetEnabledAsync(true)` even though Center M was already exactly Enabled.

That redundant mutation may be optimized separately if desired, but it is **not the cause of this failure**:

```text
08:30:17.674 Enable requested
08:30:17.957 Enable verified
```

The operation completed successfully in about 0.28 seconds.

Do not widen this small bug-fix PR with unrelated Center M transition changes unless new concrete production evidence requires it.

---

## 10. Files expected to change

Primary:

```text
src/SteamInputAddonforClaw/Install/StartupRegistration.cs
```

Focused tests, likely:

```text
tests/SteamInputAddonforClaw.Tests/WindowsTaskSchedulerStartupManagerTests.cs
```

If the production Task Scheduler store is currently not testable at the exception-classification boundary, a minimal local test seam/file is acceptable.

Avoid changes outside the startup-registration area unless compilation requires them.

---

## 11. Validation

Run the repository's normal build/test suite plus focused startup-registration tests.

Then hardware-validate the original reproduction:

```text
1. Start with the Addon installed and running.
2. Restore Center M / stock authority first.
3. Confirm physical controller is PID1901 / XInput.
4. Uninstall from Windows Installed Apps.
5. Accept any required UAC prompt.
6. Confirm the Addon Runtime exits without the 60-second safe-uninstall timeout.
7. Confirm the product is removed.
8. Confirm Center M startup roots remain stock-enabled.
9. Confirm the MSI Claw controller remains usable in stock PID1901/XInput mode.
10. Confirm the Addon startup task is absent.
```

Also validate uninstall from Full1902 Disabled/Add-on-owned mode because PR12 requires the same final stock-safe guarantee there.

Failure in real authority restoration must still block uninstall.

---

## 12. Acceptance criteria

This PR is complete only when all of the following are true:

- the exact production `FileNotFoundException (0x80070002)` missing-task result is treated as verified absence;
- no broader exception is reclassified as absence;
- post-delete independent verification remains mandatory;
- the existing fail-closed tests remain valid;
- the 2026-10-03 already-stock uninstall scenario no longer leaves the Runtime alive because of false startup-task read-back failure;
- no timeout increase is used to mask the defect;
- no new controller/startup authority or unnecessary abstraction is introduced;
- full build/tests pass;
- hardware uninstall leaves the machine in verified stock-safe state.

---

## 13. Design principle

Keep the existing Full1902 safety model.

The correct fix is:

```text
same exact task owner
+ same exact delete operation
+ same independent read-back
+ correct classification of the real Windows "task not found" result
```

not:

```text
more timeouts
more retries
more state
or weaker verification
```
