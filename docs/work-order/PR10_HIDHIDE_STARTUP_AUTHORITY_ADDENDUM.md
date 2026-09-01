# Work Order Addendum — PR10: HidHide Authority Normalization and Mandatory Startup Registration

## Status

This is an implementation addendum to:

- `docs/work-order/PR10_PHYSICAL_DEVICE_LOSS_PNP_RETURN_RECOVERY_WORK_ORDER.md`

It is required in the same PR because real MSI Claw validation found two concrete blockers before the current Full1902 path can complete the first normal Disabled transition/boot:

1. official HidHide application entries are incorrectly treated as foreign configuration; and
2. first creation of the mandatory Addon startup task can fail with `E_ACCESSDENIED` when no task exists yet.

Before implementation, read and treat as current policy authority:

- `docs/Full 1902 Implementation/FULL_1902_IMPLEMENTATION_ARCHITECTURE.md`
- `docs/Full 1902 Implementation/REBOOT_BOUND_CONTROLLER_AUTHORITY_AND_HIDHIDE_DESIGN.md`
- `docs/Full 1902 Implementation/HIDHIDE_AND_STARTUP_AUTHORITY_POLICY_REVISION_2026-09-01.md`
- the original PR10 work order above.

Where older Full1902 work orders conflict with the 2026-09-01 policy revision on foreign HidHide preservation/admission, the policy revision wins.

Do not redesign the original PnP-return recovery scope. Implement these fixes through the existing HidHide/startup/transition owners.

---

## 1. Real hardware evidence to address

Observed HidHide Applications state:

```text
HidHideCLI.exe
HidHideClient.exe
SteamInputAddonforClaw.exe
```

The current Full1902 baseline classified this as:

```text
ForeignWhitelistEntry
WhitelistCount=3
```

The two additional entries are official HidHide tools and must be part of the required Addon baseline.

Separately, on a machine where the Addon Task Scheduler entry had never been created:

```text
RegisterTaskDefinition(...)
→ E_ACCESSDENIED / 0x80070005
```

This means first startup-task creation itself needs a supported elevated repair/create seam; an existing-task idempotence change alone is insufficient.

---

## 2. Revised Disabled-mode HidHide contract

When the user chooses Addon Controller Mode, and on every boot/recovery where Center M startup roots are exactly Disabled, the Addon owns the effective HidHide controller-isolation configuration.

The desired Applications baseline is exactly:

```text
verified official HidHideCLI.exe
verified official HidHideClient.exe
current SteamInputAddonforClaw.exe
```

The desired Hidden Devices baseline is:

```text
before exact PID1902 target is known:
  []

after exact target is proven:
  [exact Addon-owned PID1902 primary gamepad collection]
```

Global state:

```text
Inverse=false
Active=true
```

Do not require manual HidHide cleanup by the user.

---

## 3. Official HidHide entries

Use the existing trusted HidHide install-location evidence / `HidHideTrustedApplicationPathResolver` as the basis for canonical official paths.

Require both roles:

```text
HidHideCLI
HidHideClient
```

Do not trust filename-only matches from arbitrary whitelist entries.

### Missing registration

If the official file path is resolvable but its HidHide Applications registration is absent:

```text
CLI missing from Applications
→ AddApplication(CliPath)

Client missing from Applications
→ AddApplication(ClientPath)
```

If the Addon entry is missing:

```text
→ AddApplication(CurrentAddonPath)
```

Read back and verify all three.

### Scope limit

Do not implement installer/package repair because `HidHideCLI.exe` or `HidHideClient.exe` is physically missing from disk.

That is not part of PR10.

If a required official canonical path cannot be resolved under the current supported HidHide install contract, fail closed as prerequisite unavailable.

---

## 4. Normalize other whitelist entries away

While establishing/reconciling Addon authority:

```text
keep:
  verified official HidHideCLI
  verified official HidHideClient
  current Addon Runtime

remove:
  every other HidHide Applications entry
```

Do not identify whether the removed entry belongs to HHC, ClawTweaks, DS4Windows, an old Addon installation, a manually added app, or something else.

Do not backup it.

Do not restore it later.

The existence of the entry is not itself an error.

A failed removal/readback is an error.

---

## 5. Normalize other hidden-device entries away

While entering/reconciling Addon authority:

```text
requestedTargets = []
→ remove every existing hidden-device entry

requestedTargets = [ownedTarget]
→ retain/add ownedTarget
→ remove every other hidden-device entry
```

Preserve the existing exact-target safety rules:

- no VID/PID wildcard;
- no entire PID1902 tree;
- no PID1901 hiding;
- no VIIPER targets.

### Important distinction from PR10 target migration

This normalization does **not** authorize migration when PR10 resolves the same returned physical device to a different exact primary PID1902 collection than committed `_ownedHiddenTarget`.

That case remains:

```text
RecoveredTargetChanged
→ fail closed
→ no target migration in PR10
```

Removing unrelated foreign/stale hidden entries and changing the committed owned target are different operations.

---

## 6. Update `AddonControllerHidHideBaseline`

Revise the Full1902 baseline primitive so readable/mutable foreign state is normalization input rather than an immediate `Conflict`.

Preferred conceptual Disabled apply:

```text
Inspect
→ require supported/readable HidHide
→ resolve exact official CLI + Client paths
→ remove unwanted Applications entries
→ add missing CLI
→ add missing Client
→ add missing Addon Runtime
→ remove unwanted Hidden entries
→ add requested exact target(s)
→ normalize Inverse=false
→ normalize Active=true
→ re-inspect
→ verify exact requested baseline
```

Keep mutation/readback behavior idempotent.

Already compliant state must not churn the driver configuration unnecessarily.

Do not add a new HidHide manager or policy engine.

---

## 7. Every Disabled boot must reconcile, not merely inspect

Current persistent HidHide state cannot be assumed correct just because it survived the previous shutdown.

A user or another application may change it while the Runtime is absent.

For every exact Disabled boot:

```text
Center M == Disabled
→ normalize zero-target or known-target baseline as appropriate
→ verify CLI registration
→ verify Client registration
→ verify Addon registration
→ verify no extra whitelist entries
→ verify expected exact hidden target set
→ verify Inverse=false
→ verify Active=true
→ only then allow live physical/virtual controller presentation
```

If normalization cannot be proven, fail closed before live controller output.

Do not ask which application changed the state.

---

## 8. Preserve two-stage first Disabled boot

Before the exact PID1902 collection is known:

```text
Applications = CLI + Client + Addon
Hidden       = []
Inverse      = false
Active       = true
→ verify
```

Then:

```text
acquire/reconcile supported physical MSI Claw to PID1902
→ resolve exact primary collection
→ Applications = CLI + Client + Addon
→ Hidden = [exactTarget]
→ Inverse=false
→ Active=true
→ verify
→ start/restart DirectInput
→ presentation attach/reconcile
```

Do not attach the virtual controller before exact physical isolation is proven.

---

## 9. PR8/PR9/PR10 recovery uses the same normalization

Before restarting DirectInput after owned input loss/PnP return:

```text
strong same-device identity proven
→ exact committed target proven
→ fresh Center M Disabled check at existing mutation boundary
→ normalize/verify deterministic Disabled HidHide baseline
→ only then restart DirectInput
```

If another app changed HidHide while the controller was absent, normalize that state away.

Do not introduce a separate HidHide recovery owner.

---

## 10. Center M Enable cleanup

`Enable Center M and Restart` is intentionally not a historical restore operation.

Remove/clear only the Addon's current authority state:

```text
remove current Addon Runtime whitelist entry
remove exact Addon-owned hidden target(s) known to current owner
ensure Inverse=false
ensure Active=false
preserve verified official HidHideCLI entry
preserve verified official HidHideClient entry
```

Do not reconstruct any entries removed when Addon mode was entered.

Do not add snapshots such as:

```text
PreviousApplications[]
PreviousHiddenDevices[]
PreviousActiveState
ThirdPartyOwnerMap
```

If another application has added its own entries while Addon mode is active, the Addon does not need to identify or restore that application's policy. Other applications own their own future startup/reconciliation.

Enable must not be permanently blocked merely because an unrelated current HidHide entry exists.

The Addon must still prove its own cleanup and required global `Active=false`, `Inverse=false` state before completing the authority release.

---

## 11. Mandatory startup-task first creation

The Disable transition must prove that the Addon Runtime will start at the next interactive logon before Center M startup roots are disabled.

A clean machine may have no Addon startup task.

Required behavior:

```text
startup task exists and exact contract matches
→ Success without rewriting it

startup task missing/drifted
→ create/repair
→ read back
→ exact verification
→ Success
```

The current first-use `E_ACCESSDENIED` path must be fixed.

---

## 12. Existing task verification

Before mutating an existing task, inspect it.

At minimum verify:

```text
Task name   = Steam Input Addon for Claw
Enabled     = true
Executable  = stable current SteamInputAddonforClaw.exe
Arguments   = --background
Trigger     = current-user interactive logon
Logon type  = InteractiveToken
Run level   = least privilege / non-elevated
```

If exact:

```text
→ return Success
→ no RegisterTaskDefinition write
→ no elevation
```

This must be the normal path after the first successful creation.

---

## 13. Missing/drifted task privileged repair

If the task is missing or materially drifted and the normal Runtime cannot create/repair it:

```text
→ invoke one bounded elevated helper operation
→ create/repair only the fixed Addon task
→ helper exits
→ normal Runtime independently re-reads Task Scheduler
→ exact contract must verify
```

Prefer the existing bounded `Verb=runas` + current-user named-pipe helper pattern.

If smallest, extend the existing privileged startup-configuration helper with one fixed operation such as:

```text
EnsureAddonStartupTask
```

Do not expose arbitrary task name/path/arguments supplied by a caller as a generic admin API.

The privileged side must only manage the known Addon task contract.

Do not add:

- a persistent admin Runtime;
- a service;
- a long-lived broker;
- a second startup manager;
- an additional controller authority state.

---

## 14. Disable ordering

Preserve fail-safe ordering:

```text
Disable and Restart requested
→ read-only transition/admission safety checks
→ establish + verify mandatory Addon startup task
→ normalize + verify zero-target Disabled HidHide baseline
→ disable + verify Center M startup roots
→ reboot
```

If mandatory startup cannot be proven:

```text
→ Center M remains Enabled
→ no authority handoff
```

If UAC is cancelled:

```text
→ fail transition
→ leave Center M Enabled
```

If startup registration succeeds and a later stage fails, do not add complex rollback solely to remove the safe Addon startup task.

---

## 15. Required HidHide tests

At minimum add/update tests for:

### Official entries

```text
CLI + Client + Addon present
→ idempotent success

CLI registration missing
→ add CLI
→ verify

Client registration missing
→ add Client
→ verify

Addon registration missing
→ add Addon
→ verify
```

### Foreign/stale normalization

```text
extra Applications entry
→ remove
→ no Conflict solely because it existed

extra Hidden entry
→ remove
→ no Conflict solely because it existed
```

### Exact final state

```text
zero-target apply
→ exactly CLI + Client + Addon
→ Hidden=[]
→ Active=true
→ Inverse=false

known-target apply
→ exactly CLI + Client + Addon
→ Hidden=[ownedTarget]
→ Active=true
→ Inverse=false
```

### Failures

```text
AddApplication fails
RemoveApplication fails
AddHiddenDevice fails
RemoveHiddenDevice fails
Active/Inverse mutation fails
post-write inspection differs/unavailable
```

All must fail closed.

### Enable

Prove:

```text
Addon registration removed
official CLI preserved
official Client preserved
owned exact target removed
Active=false
Inverse=false
no historical third-party restore
```

Do not add actual missing-HidHide-file repair tests in PR10.

---

## 16. Required startup-task tests

At minimum:

### Existing compliant task

```text
Synchronize(true)
→ exact task found
→ zero RegisterTaskDefinition writes
→ zero elevated repair
→ Success
```

### First creation

```text
task missing
→ create/repair through bounded privileged path when required
→ normal readback exact
→ Success
```

### Drift repair

Cover materially wrong:

- executable;
- arguments;
- Enabled state;
- logon trigger/principal/run-level contract.

Repair must succeed only after exact readback.

### Failure

```text
UAC cancelled
helper missing/unavailable
helper reports completion but readback wrong
readback unavailable
```

must prevent Center M Disable.

### Idempotent second boot

```text
first creation succeeded
→ next startup exact task found
→ no rewrite/elevation
```

---

## 17. Required Disabled-boot integration behavior

Add/update an integration-level test for:

```text
Center M == Disabled

initial HidHide:
  official CLI registration missing
  official Client registration missing
  Addon registration missing or present
  arbitrary extra whitelist entry
  arbitrary extra hidden entry
  Active=false
  supported Inverse drift

→ Runtime reconciliation

final HidHide:
  exactly CLI + Client + Addon
  expected exact owned hidden target set
  Active=true
  Inverse=false

→ only after verification can physical/presentation activation proceed
```

Also prove:

```text
normalization verification fails
→ no live DirectInput/presentation activation
```

Do not create instruction-level timing tests unrelated to normal handheld lifecycle.

---

## 18. Hardware validation additions for PR10

After implementation, before continuing to later Full1902 hardening, validate on a supported MSI Claw.

### A. Current real state

Start from:

```text
Applications:
  HidHideCLI
  HidHideClient
  SteamInputAddonforClaw
```

Expected:

```text
Disable and Restart
→ no ForeignWhitelistEntry
```

### B. Missing official Applications registration

Manually remove/uncheck CLI registration, then attempt the relevant Disabled admission/reconcile.

Expected:

```text
Addon restores CLI registration
→ readback verified
```

Repeat for Client.

This test is only about registration, not physically deleting the exe files.

### C. Extra application entry

Add one harmless extra application entry.

Expected:

```text
Addon normalizes it away
→ exact baseline verified
→ no manual cleanup required
```

### D. First startup task

Ensure the Addon startup task does not exist.

Expected:

```text
Disable
→ task created through supported path
→ normal Runtime readback verifies exact task
→ HidHide normalized
→ Center M roots Disabled
→ reboot
```

### E. Disabled boot

After reboot:

```text
mandatory Addon Runtime starts
→ Center M exactly Disabled
→ HidHide normalized again
→ PID1902 ownership established/reconciled
→ exact target isolation proven
→ DirectInput/presentation becomes live
```

### F. Original PR10 PnP return

Then perform the original physical disappearance/return validation unchanged.

The PnP arrival event remains only a retry trigger; strong physical identity and the committed target remain the mutation authority.

---

## 19. Explicit non-goals

Do not add:

- HidHide third-party configuration backup;
- HidHide third-party configuration restore/reconstruction;
- third-party application ownership maps;
- coexistence arbitration;
- HidHideCLI/HidHideClient file repair/reinstallation;
- a generic administrator Task Scheduler API;
- persistent elevated helper/service;
- new recovery manager/state machine;
- PnP polling;
- retry queues;
- epochs/generations/barriers;
- Fast User Switching/RDP/multi-session support.

The supported product remains one Windows user and one interactive session.

---

## 20. Final acceptance criteria added to PR10

PR10 is not ready for the next Full1902 lifecycle step until all of the following are true:

1. official HidHide CLI and Client are required baseline Applications entries;
2. missing CLI/Client **registration** is restored automatically;
3. actual missing CLI/Client files are not repaired in PR10;
4. extra whitelist entries are normalized away while Addon authority is active;
5. unrelated hidden entries are normalized away while Addon authority is active;
6. no third-party configuration backup/restore mechanism exists;
7. every Disabled boot re-normalizes and verifies HidHide before live controller activation;
8. recovery reuses the same deterministic HidHide baseline;
9. changed committed PID1902 target still fails closed as `RecoveredTargetChanged` in PR10;
10. Center M Enable removes Addon-owned state, preserves official CLI/Client entries, and does not reconstruct prior third-party state;
11. a clean machine with no Addon startup task can establish the mandatory task through a bounded elevated path;
12. an already-compliant task is verified without rewrite/elevation;
13. Center M is never disabled until the startup task and zero-target HidHide baseline have both been proven;
14. the original PR10 PnP arrival recovery remains event-driven and reuses the existing physical owner;
15. no new generalized authority/recovery/task-scheduler abstraction is introduced;
16. repository builds/tests pass; and
17. supported MSI Claw hardware completes the first end-to-end path:

```text
Center M Enabled
→ Disable and Restart
→ mandatory Addon startup verified
→ zero-target HidHide baseline normalized
→ Center M Disabled
→ reboot
→ Disabled Runtime starts
→ HidHide normalized/verified again
→ PID1902 + exact physical isolation
→ DirectInput
→ X360/SteamDeck presentation
```

without requiring the user to manually edit HidHide or Task Scheduler.
