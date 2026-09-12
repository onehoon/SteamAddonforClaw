# Battery PR1.1 — Automated Hardware Validation Runner Work Order

> **Target:** SteamAddonforClaw `main` after PR #511  
> **Scope:** Developer-only automated validation for the already-implemented MSI Claw battery charge-limit backend  
> **Product status:** Pre-release / physical-hardware validation tooling  
> **Architecture basis:** Full PID1902 standalone application. CTW integration is out of scope.  
> **Primary RE source:** `docs/RE_MSI_BatteryChargeLimit.md`

---

## 1. Goal

Add one developer-facing automated validation flow to the existing `Battery Charge Limit Test` page so a developer can press a single **Start Validation** button and exercise the PR1 battery charge-limit backend across the complete supported product range.

The runner must:

- capture the initial authoritative WMI state;
- execute the supported charge-limit values in deterministic order;
- verify every write through the existing mutation RPC/backend readback path;
- validate the enable/disable bit independently from the remembered percentage;
- stop immediately on the first failed or unverifiable hardware operation;
- write a separate human-readable validation result file under the Addon's canonical log directory;
- restore the original state when and only when it can be restored through the existing supported product API without inventing a raw-write backdoor;
- leave all normal controller, HidHide, VIIPER, routing, TDP, fan, Profile, QAM, Overlay, and Center M authority behavior untouched.

This is a focused PR1.1 validation improvement, not the production battery setting implementation.

---

## 2. Current implementation contract

PR #511 already provides the production-quality narrow hardware seam:

```text
Frontend Developer page
    -> typed frontend RPC
    -> InProcessAddonFrontendControl
    -> MsiClawBatteryChargeLimitHardware
    -> existing shared IMsiClawTdpTransport
    -> helper / MSI ACPI WMI Get_Data + Set_Data
    -> block 215
```

Current supported product policy:

```text
Allowed limits: 60, 65, 70, 75, 80, 85, 90, 95, 100
Step:           5
Enable bit:     bit 7
Limit value:    bits 0..6
```

Current backend semantics must remain authoritative:

```text
SetPercent(percent)
    read current raw value
    preserve enable bit
    write requested lower-seven-bit value
    mandatory readback
    verify enabled state + exact percentage

SetEnabled(enabled)
    read current raw value
    preserve remembered percentage
    write only enable-bit transition
    mandatory readback
    verify enabled state + exact percentage
```

Do not replace this with a second direct WMI implementation in the UI or runner.

---

## 3. Non-goals

Do **not** add any of the following in this PR:

- normal Device-page battery UI;
- persisted desired battery state;
- startup/restart reconciliation;
- sleep/hibernate/resume reconciliation;
- periodic polling;
- per-game Profile support;
- QAM support;
- Overlay support;
- CTW integration;
- raw arbitrary block-215 write RPC;
- new helper process;
- new hardware owner/manager;
- new global scheduler;
- new controller authority state;
- generalized diagnostic framework;
- rollback state machine;
- retry loops intended to hide a real WMI failure.

This runner exists only to validate the already-implemented battery backend on physical MSI Claw hardware.

---

## 4. Required UI change

Keep all existing manual controls:

- Refresh
- limit ComboBox
- Apply Limit
- Enable
- Disable

Add a compact validation section to `BatteryChargeLimitTestPage`.

Recommended layout:

```text
Automated Validation
Runs the full supported 60–100% sequence and records each verified hardware result.

[Start Validation]

Status: Idle
Progress: —
Current step: —
Result log: —
```

While the validation is running:

```text
Status: Running
Progress: 7 / 22
Current step: Enabled / 85%
Result log: ...\BatteryChargeLimitValidation-....txt
```

Terminal examples:

```text
Status: PASS
Progress: 22 / 22
Current step: Completed
Result log: C:\...\BatteryChargeLimitValidation-2026-09-12-141530.txt
```

or:

```text
Status: FAIL — VerificationFailed
Progress: 8 / 22
Current step: Set 90% while Disabled
Result log: C:\...\BatteryChargeLimitValidation-2026-09-12-141530.txt
```

### Busy policy

During automatic validation:

- disable `Start Validation`;
- disable Refresh;
- disable Apply Limit;
- disable the ComboBox;
- disable Enable/Disable;
- do not permit a second runner instance;
- restore controls when the run reaches a terminal state.

A page-local `_busy` / validation flag is sufficient. Do not add a global operation manager.

---

## 5. Validation sequence

Use a deterministic sequence and document it in code.

### Phase A — capture initial state

Call the existing capture RPC once.

Required conditions before any mutation:

```text
Snapshot.Available == true
Snapshot.RawValue != null
Snapshot.Enabled != null
Snapshot.LimitPercent != null
```

Record:

- Manufacturer
- Model
- BaseBoard
- Enabled
- LimitPercent
- RawValue
- ProductValueValid

If capture is unavailable or fails, terminate with FAIL and perform no write.

### Phase B — Disabled sweep

1. Call `SetBatteryChargeLimitTestEnabledAsync(false)`.
2. Require mutation outcome `Succeeded`.
3. Verify returned snapshot says `Enabled == false`.
4. For every supported value in ascending order:

```text
60
65
70
75
80
85
90
95
100
```

call `SetBatteryChargeLimitTestPercentAsync(value)`.

For each result require:

```text
Outcome == Succeeded
Snapshot.Enabled == false
Snapshot.LimitPercent == requested value
Snapshot.RawValue != null
Snapshot.ProductValueValid == true
```

No additional direct WMI read is necessary; the current mutation backend already performs mandatory readback and returns the authoritative readback state.

### Phase C — Enabled sweep

1. Call `SetBatteryChargeLimitTestEnabledAsync(true)`.
2. Require success and `Enabled == true`.
3. Repeat the exact supported ascending percentage sequence.

For each result require:

```text
Outcome == Succeeded
Snapshot.Enabled == true
Snapshot.LimitPercent == requested value
Snapshot.RawValue != null
Snapshot.ProductValueValid == true
```

### Phase D — explicit 100% identity check

The runner must explicitly demonstrate that these are two distinct valid states:

```text
Enabled=true,  Limit=100
Enabled=false, Limit=100
```

Recommended final check:

```text
Set 100 while Enabled
-> require Enabled=true, Limit=100
-> record enabled100Raw

Disable
-> require Enabled=false, Limit=100
-> record disabled100Raw

require enabled100Raw != disabled100Raw
```

With the documented protocol the expected raw values are normally:

```text
Enabled 100%  = 0xE4
Disabled 100% = 0x64
```

However, the runner should judge correctness from decoded authoritative fields and the fact that the raw values differ by the enable bit; it must not introduce a separate raw-value authority that bypasses the backend.

A narrow assertion such as this is acceptable:

```csharp
if (enabled100.RawValue is byte enabledRaw &&
    disabled100.RawValue is byte disabledRaw &&
    enabledRaw == disabledRaw)
{
    return Fail("100% enabled and disabled states returned the same raw byte.");
}
```

---

## 6. Fail-fast policy

The automated run is a diagnostic tool, not a stress test.

On the first operation that returns any non-success outcome:

```text
InvalidTarget
ReadFailed
WriteFailed
VerificationFailed
Unavailable
transport exception
```

stop the normal validation sequence immediately.

Do not:

- keep cycling values after a failed hardware operation;
- automatically retry a failed Set_Data or verification read;
- hide a transient failure by repeating until success;
- continue into later phases simply to produce a longer report.

The first real failure is valuable diagnostic evidence and must remain visible.

After failure, proceed only to the bounded restore rule described below when safe.

---

## 7. Initial-state restore policy

The validation changes real persistent MSI firmware state, so a successful run should restore the starting state whenever it can do so through the same supported product API.

### Restorable initial state

The initial state is restorable when:

```text
initial ProductValueValid == true
initial LimitPercent is one of 60..100 / step 5
initial Enabled is known
```

Restore sequence:

```text
SetPercent(initial.LimitPercent)
SetEnabled(initial.Enabled)
```

Why percentage first:

- `SetPercent` preserves the current enable bit;
- after that, `SetEnabled` restores the original enable state;
- both calls use normal backend read-modify-write + mandatory verification.

Require both restoration mutations to succeed and log their authoritative snapshots.

### Non-product initial value

If the initial lower-seven-bit value is outside the Addon's product set, for example 83:

```text
ProductValueValid == false
```

**Do not invent a raw-write escape hatch to restore 83.**

That would broaden the product API and weaken the fail-closed policy merely for a developer convenience path.

Instead:

```text
Restore: SKIPPED
Reason: Initial remembered limit is outside Addon supported product values.
```

The log must explicitly state that automatic restoration was not attempted.

### Restore after a failed validation step

If the initial state is restorable, attempt one normal restore sequence after a failed validation operation.

Important:

- restoration is best-effort and independently logged;
- no retries;
- if restore fails, preserve both the original validation failure and the restoration failure;
- do not replace the primary failure outcome with the restore failure.

Example:

```text
FINAL RESULT: FAIL
Primary failure: VerificationFailed at Enabled / 85%
Restore result: FAIL — ReadFailed
```

This is sufficient. Do not build a rollback coordinator or transaction abstraction.

---

## 8. Result log file

Create one standalone text file per automated validation run.

Use the existing canonical Addon log directory:

```text
AddonDataPaths.LogDirectory
```

Recommended filename:

```text
BatteryChargeLimitValidation-yyyy-MM-dd-HHmmss.fff-P{pid}.txt
```

Example:

```text
BatteryChargeLimitValidation-2026-09-12-141530.123-P12345.txt
```

Do not write these reports into the ordinary `SteamInputAddonforClaw-*.log` filename family because normal AppLog retention/parser logic owns that namespace.

### Minimum required report content

```text
Battery Charge Limit Validation
Started: 2026-09-12T14:15:30.1234567+09:00
Process: 12345
Device Manufacturer: Micro-Star International Co., Ltd.
Device Model: Claw 8 EX AI+
BaseBoard: MS-1T91

INITIAL
Available=True
Enabled=True
Limit=80
Raw=0xD0
ProductValueValid=True

[01] Disable
Outcome=Succeeded
Enabled=False
Limit=80
Raw=0x50
PASS

[02] Set 60% while Disabled
Outcome=Succeeded
Enabled=False
Limit=60
Raw=0x3C
PASS

...

[11] Enable
Outcome=Succeeded
Enabled=True
Limit=100
Raw=0xE4
PASS

[12] Set 60% while Enabled
...

[21] Verify 100% Enabled
...

[22] Verify 100% Disabled
...

RESTORE
Initial ProductValueValid=True
SetPercent(80)=Succeeded
SetEnabled(True)=Succeeded
Final Enabled=True
Final Limit=80
Final Raw=0xD0
Restore=PASS

FINAL RESULT: PASS
Completed: ...
```

Failure example:

```text
[08] Set 90% while Disabled
Outcome=VerificationFailed
FailureMessage=BatteryLimit readback did not match the requested value.
Enabled=False
Limit=85
Raw=0x55
FAIL

Sequence stopped after first failure.

RESTORE
...

FINAL RESULT: FAIL
PrimaryFailureStep=Set 90% while Disabled
PrimaryFailureOutcome=VerificationFailed
Restore=PASS
```

### File-write failure policy

A report-file write failure must not cause an extra battery mutation.

Preferred implementation:

- create/open the report before the first mutation;
- if the report cannot be created, abort before changing battery state;
- flush after every completed hardware step so evidence survives an unexpected app/process failure;
- use simple synchronous `StreamWriter` operations inside the page-local validation task; the run contains only a few dozen lines and does not need a second logging framework.

Do not route this report through `AppLog.MinimumLevelOverride`; the validation report must exist even when normal application logging is Off.

Normal AppLog may additionally emit one Info/Debug summary entry, but the standalone report is the authoritative validation artifact.

---

## 9. Recommended implementation shape

Keep the implementation local and explicit.

A small page-private runner is preferred over a new generalized service.

Example shape:

```csharp
private static readonly int[] ValidationLimits =
    [60, 65, 70, 75, 80, 85, 90, 95, 100];

private async void StartValidation_Click(object sender, RoutedEventArgs args)
{
    if (_frontend is null || _busy) return;
    await RunValidationAsync();
}
```

A tiny result helper is acceptable:

```csharp
private sealed record ValidationFailure(
    string Step,
    FrontendBatteryChargeLimitTestMutationOutcome? Outcome,
    string Message);
```

Do not create interfaces such as:

```text
IBatteryValidationRunner
IBatteryValidationLogger
IBatteryValidationPlan
IBatteryValidationStepExecutor
IBatteryRestoreCoordinator
```

unless the current code demonstrates a real second consumer. This PR has one developer page and one test sequence.

---

## 10. UI-safe execution

The current frontend RPCs are asynchronous and the backend performs hardware work off the UI thread. Reuse them directly.

The page must remain responsive while the sequence runs.

Update progress only after an operation has returned its authoritative result.

Example:

```csharp
ProgressText.Text = $"Progress: {completed} / {total}";
CurrentStepText.Text = $"Current step: {label}";
```

Do not use arbitrary delays between writes unless physical hardware validation proves the MSI WMI provider requires one.

The current backend already serializes its own read/write operations. Adding fixed `Task.Delay` calls preemptively would only make the test slower and could hide the actual API behavior we are trying to validate.

---

## 11. Navigation and cancellation policy

Do not add a complex cancellation state machine.

Simplest acceptable product behavior for this developer-only runner:

- while validation is running, disable the page Back button as well as mutation controls;
- after terminal PASS/FAIL/restore handling, re-enable Back;
- normal application shutdown may terminate the run with the process.

This avoids a page-navigation race that would otherwise allow a hidden developer page to continue mutating hardware after the user intentionally left it.

Do not add epochs/barriers/tokens solely to support leaving the page mid-sequence.

If an existing page-level lifetime token can be reused trivially, that is acceptable, but no new global cancellation authority is required.

---

## 12. Runtime / authority safety

This feature must remain independent from the Full PID1902 controller lifecycle.

The runner must not:

- request PID1901/PID1902 transitions;
- detach/attach VIIPER;
- modify HidHide;
- alter DirectInput ownership;
- reconcile controller presentation;
- change Center M authority;
- trigger restart;
- mutate Device/Profile Quick Settings;
- publish anything to QAM/Overlay.

The Full1902 Runtime remains the single controller authority when Center M is Disabled. The battery setting is an independent Device control implemented over MSI ACPI WMI.

---

## 13. Logging severity in normal AppLog

The standalone validation file is mandatory and independent from normal log-level preference.

For ordinary `AppLog` entries, keep volume low.

Suggested entries:

```csharp
AppLog.Info("BatteryValidation", "Automated validation started.", ...);
AppLog.Info("BatteryValidation", "Automated validation completed.", ...);
AppLog.Warn("BatteryValidation", "Automated validation failed.", null, ...);
```

Do not duplicate every report line into normal AppLog.

The hardware backend's existing Debug entries remain unchanged.

---

## 14. Tests

Add focused tests without building a new UI test framework.

### 14.1 Validation plan/order test

Extract only enough pure logic to verify the intended sequence if necessary.

Required assertions:

```text
Disabled
60,65,70,75,80,85,90,95,100
Enabled
60,65,70,75,80,85,90,95,100
explicit 100 enabled/disabled distinction
restore last
```

If keeping the runner fully page-local makes this awkward, a small internal static helper that returns the supported percentage list is acceptable. Do not introduce a general workflow engine.

### 14.2 Fail-fast test

Use a fake `IAddonFrontendControl` or the nearest existing practical seam.

Simulate a failure at one middle percentage, for example 85% Disabled.

Verify:

- no later validation steps execute;
- restore is attempted only when the initial state was product-valid;
- the primary failure remains the final run result.

### 14.3 Restore tests

Cover:

1. initial `Enabled=true`, `80%` -> restore calls 80 then true;
2. initial `Enabled=false`, `100%` -> restore calls 100 then false;
3. initial non-product `83%` -> no raw restore and no unsupported percentage mutation;
4. restore failure is reported separately from primary validation failure.

### 14.4 Report tests

Use a temp directory abstraction only if the page can accept a test-only directory/path seam simply.

Verify the report includes:

- device identity;
- initial raw/decoded state;
- each completed step;
- outcome/failure message;
- restore result;
- final PASS/FAIL.

Do not require byte-for-byte timestamp matching.

### 14.5 Existing regression tests

All existing tests must remain green, including:

- battery backend tests;
- helper block-215 allow-list tests;
- frontend named-pipe transport tests;
- UI architecture tests;
- Full1902 lifecycle tests;
- Overlay/QAM transport tests.

No frontend protocol bump is required if this automation is implemented entirely in the UI by composing the already-existing battery capture/mutation RPCs.

Prefer **no protocol bump**.

---

## 15. Expected files

Likely minimal implementation set:

```text
src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.xaml
src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitTestPage.xaml.cs
```

Potentially one very small helper file if it materially improves testability, for example:

```text
src/SteamInputAddonforClaw.UI/Views/BatteryChargeLimitValidationPlan.cs
```

Tests likely under:

```text
tests/SteamInputAddonforClaw.Tests/
```

Avoid changes to:

```text
MsiClawBatteryChargeLimitHardware.cs
TdpHelperProtocol.cs
NamedPipeAddonFrontendClient.cs
NamedPipeAddonFrontendServer.cs
FrontendWire.cs
controller lifecycle/ownership classes
QAM
Overlay
Profile runtime
```

unless a concrete implementation defect is discovered.

---

## 16. Suggested page-level pseudocode

```csharp
private async Task RunValidationAsync()
{
    if (_frontend is null || _busy) return;

    SetValidationBusy(true);
    ValidationFailure? primaryFailure = null;
    FrontendBatteryChargeLimitTestSnapshot? initial = null;
    string? reportPath = null;

    try
    {
        reportPath = CreateValidationReportPath();
        using var writer = CreateReportWriter(reportPath); // fail before any mutation

        initial = await _frontend.CaptureBatteryChargeLimitTestAsync();
        WriteInitial(writer, initial);

        if (!CanBegin(initial))
        {
            primaryFailure = new("Initial capture", null,
                initial.FailureMessage ?? "Initial state is unavailable.");
            WriteFailure(writer, primaryFailure);
            return;
        }

        if (!await RunEnabledTransitionAsync(false, writer))
        {
            primaryFailure = LastFailure;
            return;
        }

        foreach (var limit in ValidationLimits)
        {
            if (!await RunPercentStepAsync(limit, expectedEnabled: false, writer))
            {
                primaryFailure = LastFailure;
                return;
            }
        }

        if (!await RunEnabledTransitionAsync(true, writer))
        {
            primaryFailure = LastFailure;
            return;
        }

        foreach (var limit in ValidationLimits)
        {
            if (!await RunPercentStepAsync(limit, expectedEnabled: true, writer))
            {
                primaryFailure = LastFailure;
                return;
            }
        }

        // Explicit 100% enabled/disabled identity check.
        // Reuse normal mutation results; no raw write API.
    }
    catch (Exception exception)
    {
        primaryFailure ??= new("Transport/UI operation", null, exception.Message);
    }
    finally
    {
        if (initial is not null)
            await TryRestoreInitialStateAsync(initial, reportPath);

        RenderValidationTerminalState(primaryFailure, reportPath);
        SetValidationBusy(false);
    }
}
```

The implementation does not need to copy this exact control flow. The required behavior is the contract.

Be careful not to accidentally `return` before the `finally` restoration/report-finalization path executes.

---

## 17. Acceptance criteria

The PR is complete when all of the following are true.

### UI

- `Start Validation` exists only on the Developer battery test page.
- one click runs the full sequence automatically.
- mutation controls and Back are disabled during the run.
- progress/current step are visible.
- terminal PASS/FAIL is visible.
- report path is visible after creation.

### Hardware correctness

- initial live block-215 state is captured before any mutation;
- all values 60..100 / 5 are tested Disabled;
- all values 60..100 / 5 are tested Enabled;
- every step relies on the existing backend's mandatory readback result;
- first operation failure stops the sequence;
- no retry loop masks failures;
- 100%-Enabled and 100%-Disabled remain distinct;
- initial product-valid state is restored through existing normal APIs;
- initial non-product state never causes an unsupported raw restore write.

### Report

- separate `BatteryChargeLimitValidation-*.txt` is created in `AddonDataPaths.LogDirectory`;
- report creation failure aborts before hardware mutation;
- report is flushed after each completed hardware step;
- initial state, every completed step, first failure, restore outcome, and final result are present;
- report generation works even with normal AppLog set to Off.

### Scope safety

- no controller authority changes;
- no HidHide/VIIPER/routing changes;
- no Center M integration;
- no Device/Profile/QAM/Overlay production exposure;
- no new persistence or lifecycle reconciliation;
- no frontend protocol version bump unless absolutely required by a concrete implementation constraint;
- no new generalized validation framework or manager.

### Validation

Run at minimum:

```text
dotnet build SteamInputAddonforClaw.slnx -c Debug --no-restore
dotnet build SteamInputAddonforClaw.slnx -c Release --no-restore
dotnet test tests/SteamInputAddonforClaw.Tests/SteamInputAddonforClaw.Tests.csproj -c Debug --no-build
git diff --check
```

Then perform physical MSI Claw validation with the new Start Validation button and attach/provide the produced report file for review.

---

## 18. Physical validation expectation after merge

The first real-device run is expected to answer these questions directly:

1. Does block 215 accept every Addon product value from 60 through 100 in 5% steps?
2. Does readback exactly preserve the requested percentage?
3. Does toggling bit 7 preserve the remembered lower-seven-bit value?
4. Are 100%-Enabled and 100%-Disabled distinct and stable?
5. Do all supported MSI Claw models expose the same semantics?
6. Does any model quantize, reject, or rewrite a requested value?
7. Does any operation fail under normal helper/WMI elevation and access conditions?

If the automated report exposes a hardware discrepancy, do not compensate by adding retries or special cases immediately. First review the exact raw/decoded sequence and determine whether the firmware behavior is consistent and reproducible.

---

## 19. Overengineering guard

This PR must stay small.

The supported scenario is one developer, one Windows interactive session, one explicit validation run.

Do not defend against theoretical instruction-level races by adding:

- epochs;
- barriers;
- actor models;
- global command queues;
- new ownership state;
- transactional hardware abstractions;
- generalized workflows.

Protect only realistic behavior:

- duplicate Start clicks;
- user manually mutating while runner is active;
- leaving the page while writes continue;
- real frontend/WMI operation failure;
- process/application shutdown;
- inability to restore an unsupported initial raw value.

A page-local busy gate plus fail-fast sequential awaits is the preferred design.

---

## 20. Final implementation principle

The automated validator is not a second battery implementation.

It is only an orchestrator over the already-authoritative PR1 API:

```text
existing capture RPC
+ existing SetPercent RPC
+ existing SetEnabled RPC
+ existing mandatory backend readback
+ deterministic sequence
+ standalone evidence file
```

Keep one hardware authority, one mutation path, and one verification policy.
